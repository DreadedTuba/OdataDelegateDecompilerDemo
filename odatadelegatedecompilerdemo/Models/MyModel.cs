namespace OdataDelegateDecompilerDemo.Models
{
    using System;
    using System.Data.Entity;
    using System.Linq;
    using DelegateDecompiler;
    using System.ComponentModel.DataAnnotations.Schema;

    public class MyModel : DbContext
    {
        // Your context has been configured to use a 'MyModel' connection string from your application's 
        //configuration file (App.config or Web.config). By default, this connection string targets the 
        // 'OdataDelegateDecompilerDemo.Models.MyModel' database on your LocalDb instance. 
        // 
        // If you wish to target a different database and/or database provider, modify the 'MyModel' 
        // connection string in the application configuration file.
        public MyModel()
            : base("name=MyModel")
        {
        }

        // Add a DbSet for each entity type that you want to include in your model. For more information 
        // on configuring and using a Code First model, see http://go.microsoft.com/fwlink/?LinkId=390109.

        public virtual DbSet<MyEntity> MyEntities { get; set; }
    }

    public class MyEntity
    {
        public int Id { get; set; }
        public string Name { get; set; }

        public string someFlag { get; set; }

        [Computed]
        [NotMapped]
        public bool MyComputedProperty
        {
            get { return (someFlag == "Y") ? true : false; }
            set { someFlag = (value) ? "Y" : "N"; }
        }
    }
}