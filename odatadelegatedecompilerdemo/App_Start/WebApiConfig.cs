using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace OdataDelegateDecompilerDemo
{

    using System.Web.Http.OData.Builder;
    using System.Web.Http.OData.Extensions;
    using OdataDelegateDecompilerDemo.Models;
    public static class WebApiConfig
    {
        public static void Register(HttpConfiguration config)
        {
            // Web API configuration and services

            // Web API routes
            config.MapHttpAttributeRoutes();

            config.Routes.MapHttpRoute(
                name: "DefaultApi",
                routeTemplate: "api/{controller}/{id}",
                defaults: new { id = RouteParameter.Optional }
            );


            ODataConventionModelBuilder builder = new ODataConventionModelBuilder();
            builder.EntitySet<MyEntity>("MyEntities");
            var cu = builder.StructuralTypes.First(t => t.ClrType == typeof(MyEntity));
            cu.AddProperty(typeof(MyEntity).GetProperty("MyComputedProperty"));

            config.Routes.MapODataServiceRoute("odata", "odata", builder.GetEdmModel());

        }
    }
}
