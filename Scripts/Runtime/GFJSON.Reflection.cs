using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using UnityEngine;

namespace NightRider.GFJSON
{
    public static class Reflection
    {
        private const BindingFlags AllSerializableFields = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;

        
        #region IsGenericList()
        // From https://stackoverflow.com/a/951602/1111634
        public static bool IsGenericList( Type type )
        {
            if( type == null ) {
                Debug.LogError( "Type is null!" );
                return false;
            }

            foreach( Type inter in type.GetInterfaces() )
            {
                if( !inter.IsGenericType ) continue;
                if( inter.GetGenericTypeDefinition() == typeof( ICollection<> ) )
                {
                    // if needed, you can also return the type used as generic argument
                    return true;
                }
            }
            return false;
        }
        #endregion IsGenericList()
        
        #region IsSubclassOfRawGeneric()
        // From https://stackoverflow.com/a/457708/1111634
        public static bool IsSubclassOfRawGeneric( Type generic, Type toCheck )
        {
            while( toCheck != null && toCheck != typeof( object ) )
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if( generic == cur ) return true;

                toCheck = toCheck.BaseType;
            }
            return false;
        }
        #endregion IsSubclassOfRawGeneric()
        
        #region Type.IsClassOrStruct()
        public static bool IsClassOrStruct( this Type type )
        {
            return (type.IsClass || (type.IsValueType && !type.IsPrimitive)) && !type.IsEnum;
        }
        #endregion Type.IsClassOrStruct()
        #region Type.IsList()
        public static bool IsList( this Type type )
        {
            if( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> ) ) return true;

            //// Check if the type implements IList<TValue>
            //foreach( Type interfaceType in type.GetInterfaces() ) {
            //    if( interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof( IList<> ) ) return true;
            //}

            return false;
        }
        #endregion IsList()
        #region Type.IsHashSet()
        public static bool IsHashSet( this Type type )
        {
            if( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( HashSet<> ) ) return true;
            return false;
        }
        #endregion IsHashSet()
        #region Type.IsDictionary()
        public static bool IsDictionary( this Type type )
        {
            // Check if the type is exactly Dictionary<TKey, TValue>
            if( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( Dictionary<,> ) ) return true;

            //// Check if the type implements IDictionary<TKey, TValue>
            //foreach( Type interfaceType in type.GetInterfaces() ) {
            //    if( interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof( IDictionary<,> ) ) return true;
            //}

            return false;
        }
        #endregion IsDictionary()

        #region WalkAllSerializedFields()
        internal static void WalkAllSerializedFields( Type type, object obj, Action<string, FieldInfo, object> onField, GFJSON.Options options, bool skipNullValue = true )
        {
            if( skipNullValue && obj == null ) return;

            foreach( var field in type.GetFields( AllSerializableFields ) )
            {
                // If [NonSerialized] or [JSONSkip] attribute exists, skip this field
                if( Attribute.IsDefined( field, typeof( NonSerializedAttribute ) ) ) continue;
                if( Attribute.IsDefined( field, typeof( JSONSkipAttribute ) ) ) continue;

                // Serialize only fields which are public, or have [SerializeField] attribute
                var shouldSerialize = field.IsPublic || Attribute.IsDefined( field, typeof( SerializeField ) );
                if( !shouldSerialize ) continue;

                // Class or struct - only if it has [Serialized] attribute, or if it inherits from ScriptableObject
                var isClassOrStruct = field.FieldType.IsClassOrStruct();
                if( isClassOrStruct ) {
                    var isClassSerializable = Attribute.IsDefined( field.FieldType, typeof( SerializableAttribute ) )
                        //|| Attribute.IsDefined( field.FieldType, typeof( IsSerializedAttribute ) )
                        || Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), field.FieldType );
                    if( !isClassSerializable ) continue;
                }

                // Field name - read it from custom attribute, if present
                string fieldName;
                if( !options.HasFlag(GFJSON.Options.IgnoreJSONName) )
                {
                    var jsonName = field.GetCustomAttributes( typeof(JSONNameAttribute), false ) as JSONNameAttribute[];
                    if( jsonName != null && jsonName.Length > 0 ) {         // Use GFJSON name
                        fieldName = jsonName[0].Name;
                    } else {
                        // Check for DataMember
                        var dataMemberName = field.GetCustomAttributes( typeof( DataMemberAttribute ), false ) as DataMemberAttribute[];
                        if( dataMemberName != null && dataMemberName.Length > 0 ) {
                            fieldName = dataMemberName[0].Name;
                        } else fieldName = field.Name;
                    }

                } else fieldName = field.Name;

                onField( fieldName, field, obj != null ? field.GetValue(obj) : null );
            }
        }
        #endregion WalkAllSerializedFields()
    }
}
