using System;

namespace NightRider.GFJSON
{
    [AttributeUsage( AttributeTargets.Field, AllowMultiple = false )]
    public class JSONNameAttribute : Attribute
    {
        public string Name { get; set; }
        public JSONNameAttribute( string name ) { Name = name; }
    }

    [AttributeUsage( AttributeTargets.Field, AllowMultiple = false )]
    public class JSONSkipAttribute : Attribute { }

    [AttributeUsage( AttributeTargets.Field, AllowMultiple = false )]
    public class JavaSchemeSkipAttribute : Attribute { }
}
