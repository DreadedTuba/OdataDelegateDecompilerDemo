using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Data.Entity.Infrastructure;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.ModelBinding;
using System.Web.Http.OData;
using System.Web.Http.OData.Routing;
using OdataDelegateDecompilerDemo.Models;
using System.Web.Http.OData.Query;
using DelegateDecompiler;
using System.Web.Http.OData.Extensions;
using Microsoft.Data.Edm;
using System.Diagnostics.Contracts;
using Microsoft.Data.OData.Query;
using System.Reflection;
using System.Globalization;
using System.Net.Http.Formatting;
using System.Text;

namespace OdataDelegateDecompilerDemo.Controllers
{
    /*
    The WebApiConfig class may require additional changes to add a route for this controller. Merge these statements into the Register method of the WebApiConfig class as applicable. Note that OData URLs are case sensitive.

    using System.Web.Http.OData.Builder;
    using System.Web.Http.OData.Extensions;
    using OdataDelegateDecompilerDemo.Models;
    ODataConventionModelBuilder builder = new ODataConventionModelBuilder();
    builder.EntitySet<MyEntity>("MyEntities");
    config.Routes.MapODataServiceRoute("odata", "odata", builder.GetEdmModel());
    */
    public class MyEntitiesController : ODataController
    {
        private MyModel db = new MyModel();
        private static readonly MethodInfo _limitResultsGenericMethod = typeof(ODataQueryOptions).GetMethod("LimitResults");

        // GET: odata/MyEntities
        //[EnableQuery]
        //public IQueryable<MyEntity> GetMyEntities(ODataQueryOptions opts)
        //{
        //    IQueryable results = opts.ApplyTo(db.MyEntities.AsQueryable());
        //    return results as IQueryable<MyEntity>;
        //}

        // GET: odata/MyEntities
        //[EnableQuery]
        public IQueryable<MyEntity> GetMyEntities(ODataQueryOptions opts)
        {
            var settings = new ODataValidationSettings()
            {
                // Initialize settings as needed.
                AllowedFunctions = AllowedFunctions.AllMathFunctions
            };

            opts.Validate(settings);
            //IQueryable results = db.MyEntities.AsQueryable<MyEntity>();
            //if (opts.Filter != null)
            //{
            //    results = opts.Filter.ApplyTo(results, new ODataQuerySettings() { EnableConstantParameterization = false, EnsureStableOrdering = false });
            //}

            //results = results.Decompile();
            //results.Decompile
            //IQueryable results = MyApplyToWithDecompile(db.MyEntities.AsQueryable(), opts);
            //return results as IQueryable<MyEntity>;
            //return db.MyEntities;


            IQueryable result = db.MyEntities.AsQueryable();
            ODataQuerySettings querySettings = new ODataQuerySettings() { EnableConstantParameterization = false, EnsureStableOrdering = false };

            // Construct the actual query and apply them in the following order: filter, orderby, skip, top
            if (opts.Filter != null)
            {
                result = opts.Filter.ApplyTo(result, querySettings);
                result = ((result as IQueryable<MyEntity>).Decompile()).AsQueryable();
            }

            if (opts.InlineCount != null && Request.ODataProperties().TotalCount == null)
            {
                long? count = opts.InlineCount.GetEntityCount(result);
                if (count.HasValue)
                {
                    Request.ODataProperties().TotalCount = count.Value;
                }
            }

            OrderByQueryOption orderBy = opts.OrderBy;

            // $skip or $top require a stable sort for predictable results.
            // Result limits require a stable sort to be able to generate a next page link.
            // If either is present in the query and we have permission,
            // generate an $orderby that will produce a stable sort.
            if (querySettings.EnsureStableOrdering &&
                (opts.Skip != null || opts.Top != null || querySettings.PageSize.HasValue))
            {
                // If there is no OrderBy present, we manufacture a default.
                // If an OrderBy is already present, we add any missing
                // properties necessary to make a stable sort.
                // Instead of failing early here if we cannot generate the OrderBy,
                // let the IQueryable backend fail (if it has to).
                orderBy = orderBy == null
                            ? GenerateDefaultOrderBy(opts.Context)
                            : EnsureStableSortOrderBy(orderBy, opts.Context);
            }

            if (orderBy != null)
            {
                result = orderBy.ApplyTo(result, querySettings);
            }

            if (opts.Skip != null)
            {
                result = opts.Skip.ApplyTo(result, querySettings);
            }

            if (opts.Top != null)
            {
                result = opts.Top.ApplyTo(result, querySettings);
            }

            if (opts.SelectExpand != null)
            {
                Request.ODataProperties().SelectExpandClause = opts.SelectExpand.SelectExpandClause;
                result = opts.SelectExpand.ApplyTo(result, querySettings);
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

            return result as IQueryable<MyEntity>; // this compiles and works in most cases except when using $top with anything else.  Even if no results are found.
            //return result.AsQueryable(); // this doesn't compile, Error: Cannot implicitly convert type 'System.Linq.IQueryable' to 'System.Linq.IQueryable<MyEntity>.  An explicit conversion exists (are you missing a cast?)

        }

        // GET: odata/MyEntities(5)
        [EnableQuery]
        public SingleResult<MyEntity> GetMyEntity([FromODataUri] int key)
        {
            return SingleResult.Create(db.MyEntities.Where(myEntity => myEntity.Id == key));
        }

        // PUT: odata/MyEntities(5)
        public IHttpActionResult Put([FromODataUri] int key, Delta<MyEntity> patch)
        {
            Validate(patch.GetEntity());

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            MyEntity myEntity = db.MyEntities.Find(key);
            if (myEntity == null)
            {
                return NotFound();
            }

            patch.Put(myEntity);

            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MyEntityExists(key))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Updated(myEntity);
        }

        // POST: odata/MyEntities
        public IHttpActionResult Post(MyEntity myEntity)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            db.MyEntities.Add(myEntity);
            db.SaveChanges();

            return Created(myEntity);
        }

        // PATCH: odata/MyEntities(5)
        [AcceptVerbs("PATCH", "MERGE")]
        public IHttpActionResult Patch([FromODataUri] int key, Delta<MyEntity> patch)
        {
            Validate(patch.GetEntity());

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            MyEntity myEntity = db.MyEntities.Find(key);
            if (myEntity == null)
            {
                return NotFound();
            }

            patch.Patch(myEntity);

            try
            {
                db.SaveChanges();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!MyEntityExists(key))
                {
                    return NotFound();
                }
                else
                {
                    throw;
                }
            }

            return Updated(myEntity);
        }

        // DELETE: odata/MyEntities(5)
        public IHttpActionResult Delete([FromODataUri] int key)
        {
            MyEntity myEntity = db.MyEntities.Find(key);
            if (myEntity == null)
            {
                return NotFound();
            }

            db.MyEntities.Remove(myEntity);
            db.SaveChanges();

            return StatusCode(HttpStatusCode.NoContent);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }

        private bool MyEntityExists(int key)
        {
            return db.MyEntities.Count(e => e.Id == key) > 0;
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

        internal static IQueryable LimitResults(IQueryable queryable, int limit, out bool resultsLimited)
        {
            MethodInfo genericMethod = _limitResultsGenericMethod.MakeGenericMethod(queryable.ElementType);
            object[] args = new object[] { queryable, limit, null };
            IQueryable results = genericMethod.Invoke(null, args) as IQueryable;
            resultsLimited = (bool)args[2];
            return results;
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
