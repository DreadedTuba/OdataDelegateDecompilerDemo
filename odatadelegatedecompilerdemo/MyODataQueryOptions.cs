using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Web;
using System.Web.Http.OData;
using System.Web.Http.OData.Extensions;
using System.Web.Http.OData.Query;
using System.Web.Http;
using Microsoft.Data.Edm;
using System.Diagnostics.Contracts;
using System.Reflection;
using System.Web.Http.Dispatcher;
using System.Globalization;
using System.Text;
using System.Net.Http.Formatting;
using Microsoft.Data.OData.Query;

namespace OdataDelegateDecompilerDemo
{
    public class MyODataQueryOptions : ODataQueryOptions
    {
        private static readonly MethodInfo _limitResultsGenericMethod = typeof(ODataQueryOptions).GetMethod("LimitResults");
        private IAssembliesResolver _assembliesResolver;

        public MyODataQueryOptions(ODataQueryContext context, HttpRequestMessage request) : base(context, request)
        {

            if (request.GetConfiguration() != null)
            {
                _assembliesResolver = request.GetConfiguration().Services.GetAssembliesResolver();
            }

            // fallback to the default assemblies resolver if none available.
            _assembliesResolver = _assembliesResolver ?? new DefaultAssembliesResolver();
        }
    
        public override IQueryable ApplyTo(IQueryable query, ODataQuerySettings querySettings)
        {
            if (query == null)
            {
                throw new ArgumentNullException("query");
            }

            if (querySettings == null)
            {
                throw new ArgumentNullException("querySettings");
            }

            IQueryable result = query;

            // Construct the actual query and apply them in the following order: filter, orderby, skip, top
            if (Filter != null)
            {
                result = Filter.ApplyTo(result, querySettings, _assembliesResolver);
            }

            if (InlineCount != null && Request.ODataProperties().TotalCount == null)
            {
                long? count = InlineCount.GetEntityCount(result);
                if (count.HasValue)
                {
                    Request.ODataProperties().TotalCount = count.Value;
                }
            }

            OrderByQueryOption orderBy = OrderBy;

            // $skip or $top require a stable sort for predictable results.
            // Result limits require a stable sort to be able to generate a next page link.
            // If either is present in the query and we have permission,
            // generate an $orderby that will produce a stable sort.
            if (querySettings.EnsureStableOrdering &&
                (Skip != null || Top != null || querySettings.PageSize.HasValue))
            {
                // If there is no OrderBy present, we manufacture a default.
                // If an OrderBy is already present, we add any missing
                // properties necessary to make a stable sort.
                // Instead of failing early here if we cannot generate the OrderBy,
                // let the IQueryable backend fail (if it has to).
                orderBy = orderBy == null
                            ? GenerateDefaultOrderBy(Context)
                            : EnsureStableSortOrderBy(orderBy, Context);
            }

            if (orderBy != null)
            {
                result = orderBy.ApplyTo(result, querySettings);
            }

            if (Skip != null)
            {
                result = Skip.ApplyTo(result, querySettings);
            }

            if (Top != null)
            {
                result = Top.ApplyTo(result, querySettings);
            }

            if (SelectExpand != null)
            {
                Request.ODataProperties().SelectExpandClause = SelectExpand.SelectExpandClause;
                result = SelectExpand.ApplyTo(result, querySettings);
            }

            if (querySettings.PageSize.HasValue)
            {
                bool resultsLimited;
                result = LimitResults(result, querySettings.PageSize.Value, out resultsLimited);
                if (resultsLimited && Request.RequestUri != null && Request.RequestUri.IsAbsoluteUri && Request.ODataProperties().NextLink == null)
                {
                    Uri nextPageLink = GetNextPageLink(Request, querySettings.PageSize.Value);
                    Request.ODataProperties().NextLink = nextPageLink;
                }
            }

            return result;
        }


        internal static IQueryable LimitResults(IQueryable queryable, int limit, out bool resultsLimited)
        {
            MethodInfo genericMethod = _limitResultsGenericMethod.MakeGenericMethod(queryable.ElementType);
            object[] args = new object[] { queryable, limit, null };
            IQueryable results = genericMethod.Invoke(null, args) as IQueryable;
            resultsLimited = (bool)args[2];
            return results;
        }

        // Generates the OrderByQueryOption to use by default for $skip or $top
        // when no other $orderby is available.  It will produce a stable sort.
        // This may return a null if there are no available properties.
        private static OrderByQueryOption GenerateDefaultOrderBy(ODataQueryContext context)
        {
            string orderByRaw = String.Join(",",
                                    GetAvailableOrderByProperties(context)
                                        .Select(property => property.Name));
            return String.IsNullOrEmpty(orderByRaw)
                    ? null
                    : new OrderByQueryOption(orderByRaw, context);
        }

        // Returns a sorted list of all properties that may legally appear
        // in an OrderBy.  If the entity type has keys, all are returned.
        // Otherwise, when no keys are present, all primitive properties are returned.
        private static IEnumerable<IEdmStructuralProperty> GetAvailableOrderByProperties(ODataQueryContext context)
        {
            Contract.Assert(context != null);

            IEdmEntityType entityType = context.ElementType as IEdmEntityType;
            if (entityType != null)
            {
                IEnumerable<IEdmStructuralProperty> properties =
                    entityType.Key().Any()
                        ? entityType.Key()
                        : entityType
                            .StructuralProperties()
                            .Where(property => property.Type.IsPrimitive());

                // Sort properties alphabetically for stable sort
                return properties.OrderBy(property => property.Name);
            }
            else
            {
                return Enumerable.Empty<IEdmStructuralProperty>();
            }
        }


        /// <summary>
        /// Ensures the given <see cref="OrderByQueryOption"/> will produce a stable sort.
        /// If it will, the input <paramref name="orderBy"/> will be returned
        /// unmodified.  If the given <see cref="OrderByQueryOption"/> will not produce a
        /// stable sort, a new <see cref="OrderByQueryOption"/> instance will be created
        /// and returned.
        /// </summary>
        /// <param name="orderBy">The <see cref="OrderByQueryOption"/> to evaluate.</param>
        /// <param name="context">The <see cref="ODataQueryContext"/>.</param>
        /// <returns>An <see cref="OrderByQueryOption"/> that will produce a stable sort.</returns>
        private static OrderByQueryOption EnsureStableSortOrderBy(OrderByQueryOption orderBy, ODataQueryContext context)
        {
            Contract.Assert(orderBy != null);
            Contract.Assert(context != null);

            // Strategy: create a hash of all properties already used in the given OrderBy
            // and remove them from the list of properties we need to add to make the sort stable.
            HashSet<string> usedPropertyNames =
                new HashSet<string>(orderBy.OrderByNodes.OfType<OrderByPropertyNode>().Select(node => node.Property.Name));

            IEnumerable<IEdmStructuralProperty> propertiesToAdd = GetAvailableOrderByProperties(context).Where(prop => !usedPropertyNames.Contains(prop.Name));

            if (propertiesToAdd.Any())
            {
                // The existing query options has too few properties to create a stable sort.
                // Clone the given one and add the remaining properties to end, thereby making
                // the sort stable but preserving the user's original intent for the major
                // sort order.
                orderBy = new OrderByQueryOption(orderBy.RawValue, context);
                foreach (IEdmStructuralProperty property in propertiesToAdd)
                {
                    orderBy.OrderByNodes.Add(new OrderByPropertyNode(property, OrderByDirection.Ascending));
                }
            }

            return orderBy;
        }

        internal static Uri GetNextPageLink(HttpRequestMessage request, int pageSize)
        {
            Contract.Assert(request != null);
            Contract.Assert(request.RequestUri != null);
            Contract.Assert(request.RequestUri.IsAbsoluteUri);

            return GetNextPageLink(request.RequestUri, request.GetQueryNameValuePairs(), pageSize);
        }

        internal static Uri GetNextPageLink(Uri requestUri, int pageSize)
        {
            Contract.Assert(requestUri != null);
            Contract.Assert(requestUri.IsAbsoluteUri);

            return GetNextPageLink(requestUri, new FormDataCollection(requestUri), pageSize);
        }

        internal static Uri GetNextPageLink(Uri requestUri, IEnumerable<KeyValuePair<string, string>> queryParameters, int pageSize)
        {
            Contract.Assert(requestUri != null);
            Contract.Assert(queryParameters != null);
            Contract.Assert(requestUri.IsAbsoluteUri);

            StringBuilder queryBuilder = new StringBuilder();

            int nextPageSkip = pageSize;

            foreach (KeyValuePair<string, string> kvp in queryParameters)
            {
                string key = kvp.Key;
                string value = kvp.Value;
                switch (key)
                {
                    case "$top":
                        int top;
                        if (Int32.TryParse(value, out top))
                        {
                            // There is no next page if the $top query option's value is less than or equal to the page size.
                            Contract.Assert(top > pageSize);
                            // We decrease top by the pageSize because that's the number of results we're returning in the current page
                            value = (top - pageSize).ToString(CultureInfo.InvariantCulture);
                        }
                        break;
                    case "$skip":
                        int skip;
                        if (Int32.TryParse(value, out skip))
                        {
                            // We increase skip by the pageSize because that's the number of results we're returning in the current page
                            nextPageSkip += skip;
                        }
                        continue;
                    default:
                        break;
                }

                if (key.Length > 0 && key[0] == '$')
                {
                    // $ is a legal first character in query keys
                    key = '$' + Uri.EscapeDataString(key.Substring(1));
                }
                else
                {
                    key = Uri.EscapeDataString(key);
                }
                value = Uri.EscapeDataString(value);

                queryBuilder.Append(key);
                queryBuilder.Append('=');
                queryBuilder.Append(value);
                queryBuilder.Append('&');
            }

            queryBuilder.AppendFormat("$skip={0}", nextPageSkip);

            UriBuilder uriBuilder = new UriBuilder(requestUri)
            {
                Query = queryBuilder.ToString()
            };
            return uriBuilder.Uri;
        }
    }

}