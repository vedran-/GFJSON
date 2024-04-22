//#define SUPPORT_OBSCURED_VALUES

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
#if SUPPORT_OBSCURED_VALUES
    using Beebyte.Obfuscator;
#endif

namespace NightRider.GFJSON
{
    public static class GFJSON
    {
        [Flags]
        public enum Options
        {
            // Serializes similar to Unity JSON serializer
            None                        = 0,

            // Ignore [JSONName] attribute
            IgnoreJSONName              = 0x0001,

            // Export references and enums as human-readable names
            HumanReadableFormat         = 0x0002,

            // If set, it will also reference ScriptableObjects
            ReferenceScriptableObjects  = 0x0004,

            // If set, it will also serialize Dictionary<,>
            SerializeDictionaries       = 0x0008,
#if JSON_HASHSET_SUPPORT
            // If set, it will also serialize HashSet<>
            SerializeHashSet            = 0x0016,
#endif
        }

        #region class JSONObject
        [Serializable]
        public class JSONObject
        {
            // NOTE: Only one of these 3 variables should have value, the rest should be null
            public string                       value = null;       // For direct values (e.g. int, string, bool, ...)
            public Dictionary<string, JSONObject>   objects = null;     // For classes and structs
            public List<JSONObject>                 list = null;        // For arrays and lists

            #region ToString()
            public void ToString( StringBuilder sb )
            {
                if( value != null )
                {
                    sb.Append( $"\"{value}\"" );
                } else if( objects != null )
                {
                    sb.Append( "{ " );
                    bool isFirst = true;
                    foreach( var key in objects.Keys ) {
                        if( !isFirst ) sb.Append( ", " );
                        sb.Append( $"\"{key}\": " );
                        objects[key].ToString( sb );
                        isFirst = false;
                    }
                    sb.Append( " }" );
                } else if( list != null )
                {
                    sb.Append( "[ " );
                    for( int i = 0; i < list.Count; i++ ) {
                        if( i > 0 ) sb.Append( ", " );
                        list[i].ToString( sb );
                    }
                    sb.Append( " ]" );
                } else sb.Append( "<NULL>" );
            }

            public override string ToString()
            {
                var sb = new StringBuilder();
                ToString( sb );
                return sb.ToString();
            }
            #endregion ToString()
        }
        #endregion class JSONObject

        #region [API] Parse()
        public static JSONObject Parse( string json ) => Parse( new StringParser( json ) );
        public static JSONObject Parse( StringParser sp )
        {
            if( sp.EOF() ) return null;

            var obj = new JSONObject();

            var keyword = sp.GetJSONKeyword();
            if( keyword == "{" )            // *** Parse object
            {
                obj.objects = new Dictionary<string, JSONObject>();

                keyword = sp.GetJSONKeyword();
                while( !sp.EOF() )
                {
                    if( keyword == "}" ) break; // Properly finished with list

                    var name = keyword;
                    keyword = sp.GetJSONKeyword();
                    if( keyword != ":" ) {
                        Debug.LogError( $"Expecting ':' parsing JSON at position {sp.Position}, but got '{keyword}' instead (at '{name}')!" );
                    }

                    // Parse & add object
                    var child = Parse( sp );
                    Debug.Assert( !obj.objects.ContainsKey( name ), $"JSON at position {sp.Position} - we already have field with name '{name}'" );
                    obj.objects[name] = child;

                    // Get next keyword
                    keyword = sp.GetJSONKeyword();
                    if( keyword == "," ) {          // Continue to next element in object
                        keyword = sp.GetJSONKeyword();
                    } else if( keyword == "}" ) {   // Reached end of object
                        break;
                    } else {
                        Debug.LogError( $"Invalid keyword '{keyword}' at position {sp.Position} in JSON object: expecting , or }}" );
                    }
                }

            } else if( keyword == "[" )     // *** Parse array
            {
                obj.list = new List<JSONObject>();

                if( sp.PeekNextJSONKeyword() == "]" ) {     // Properly finished with list
                    sp.GetJSONKeyword();    // Skip ']'
                    return obj;
                }

                while( !sp.EOF() )
                {
                    // Parse & add object
                    var child = Parse( sp );
                    obj.list.Add( child );

                    // Get next keyword
                    keyword = sp.GetJSONKeyword();
                    if( keyword == "," ) {          // Continue to next element in array
                        continue;
                    } else if( keyword == "]" ) {   // Reached end of array
                        break;
                    } else {
                        Debug.LogError( $"Invalid keyword '{keyword}' at position {sp.Position} in JSON array: expecting , or ]" );
                    }
                }
            } else {
                if( sp.Data[sp.LastPosition] == '\"' ) obj.value = keyword;
                else if( keyword == "null" ) obj.value = null;
                else obj.value = keyword;
            }

            return obj;
        }
        #endregion Parse()

        #region [API] DeserializeToNewObject<T>()
        /// <summary>
        /// Deserializes JSON string into new object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="jsonStr"></param>
        /// <param name="options">Serialization/deserialization options</param>
        /// <returns></returns>
        public static T DeserializeToNewObject<T>( string jsonStr, Options options = Options.None )
        {
            var json = Parse( jsonStr );
            if( json == null ) {
                Debug.LogError( "Error parsing JSON string!" );
                return default(T);
            }
            return (T)Deserialize( json, typeof(T), true, options );
        }

        public static T DeserializeToNewObject<T>( GFJSON.JSONObject json, Options options = Options.None )
        {
            if( json == null )
            {
                Debug.LogError( "DeserializeToNewObject: JSON object is null!" );
                return default( T );
            }
            return (T)Deserialize( json, typeof( T ), true, options );
        }

        #endregion DeserializeToNewObject()
        #region [API] DeserializeInto<T>()
        /// <summary>
        /// Deserializes JSON string into existing object.
        /// It will reuse object (and its child objects) as much as possible when serializing again
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="jsonStr"></param>
        /// <param name="obj">Into which object to deserialize</param>
        /// <param name="options">Serialization/deserialization options</param>
        /// <returns></returns>
        public static bool DeserializeInto<T>( string jsonStr, T obj, Options options = Options.None ) where T : class
        {
            var json = Parse( jsonStr );
            if( json == null ) {
                Debug.LogError( "Error parsing JSON string!" );
                return false;
            }

            Reflection.WalkAllSerializedFields( typeof(T), obj, ( fieldName, fieldInfo, fieldObj ) =>
            {
                var childRoot = json.objects.ContainsKey(fieldName) ? json.objects[fieldName] : null;
                if( childRoot == null ) return; // This field doesn't appear in JSON, so just skip it

                fieldObj = Deserialize( childRoot, fieldInfo.FieldType, false, options, fieldObj );
                fieldInfo.SetValue( obj, fieldObj );
            }, options );

            return true;
        }
        #endregion DeserializeInto()

        #region [Util] Serialize()
        private static void Serialize( Type type, object obj, StringBuilder sb, Options options )
        {
            var appendComma = false;

            // Serialize all the fields inside object
            Reflection.WalkAllSerializedFields( type, obj, (fieldName, field, value) =>
            {
                if( appendComma ) sb.Append( "," );
                appendComma = true;

                // Append field name
                sb.Append( $"\"{fieldName}\":" );

                SerializeField( sb, field.FieldType, obj != null ? field.GetValue( obj ) : null, options );
            }, options );
        }
        #endregion Serialize()

        #region [API] Serialize<T>()
        /// <summary>
        /// Serializes any (Unity serializable) object to a JSON string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="options">Serialization/deserialization options</param>
        /// <returns></returns>
        public static string Serialize<T>( T obj, Options options = Options.None )
        {
            var sb = new StringBuilder();
            sb.Append( "{" );
            Serialize( typeof(T), obj, sb, options );
            sb.Append( "}" );
            return sb.ToString();
        }
        #endregion Serialize<T>()
        #region [API] SerializeField()
        public static string SerializeField( Type fieldType, object fieldObj, Options options = GFJSON.Options.None )
        {
            var sb = new StringBuilder();
            SerializeField( sb, fieldType, fieldObj, options );
            return sb.ToString();
        }
        public static void SerializeField( StringBuilder sb, Type fieldType, object fieldObj, Options options )
        {
            if( fieldObj == null ) {    // Check for null
                sb.Append( "null" );
                return;
            }
            if( fieldType == null ) {   // This should never happen if the code is correct, but just in case
                Debug.LogError( $"No fieldType for obj {fieldObj}" );
                return;
            }

            if( fieldType == typeof(string) )                   // *** String
            {
                sb.Append($"\"{StringParser.Escape(fieldObj as string)}\"");
            } else if( fieldType == typeof(char) )              // *** Char
            {
                sb.Append( $"\"{StringParser.Escape(((char)fieldObj).ToString())}\"" );
#if SUPPORT_OBSCURED_VALUES
            } else if( fieldType == typeof(ObscuredBool) )      // *** ObscuredBool
            {
                bool value = (bool)((ObscuredBool)fieldObj);
                sb.Append( value ? "1": "0" );
            } else if( fieldType == typeof(ObscuredInt) ) {     // *** ObscuredInt
                int value = (int)((ObscuredInt)fieldObj);
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(ObscuredLong) ) {    // *** ObscuredLong
                long value = (long)((ObscuredLong)fieldObj);
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(ObscuredFloat) ) {   // *** ObscuredFloat
                float value = (float)((ObscuredFloat)fieldObj);
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(ObscuredDouble) ) {  // *** ObscuredDouble
                double value = (double)((ObscuredDouble)fieldObj);
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
#endif // SUPPORT_OBSCURED_VALUES

            } else if( fieldType == typeof(bool) )              // *** Boolean - serialize it as int, to save on space
            {
                sb.Append( ((bool)fieldObj) ? "1" : "0" );
            } else if( fieldType == typeof(byte) ) {            // *** byte
                byte value = (byte)fieldObj;
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(int) ) {             // *** int
                int value = (int)fieldObj;
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(long) ) {            // *** long
                long value = (long)fieldObj;
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(float) ) {           // *** float
                float value = (float)fieldObj;
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType == typeof(double) ) {          // *** double
                double value = (double)fieldObj;
                sb.Append( value.ToString( CultureInfo.InvariantCulture ) );
            } else if( fieldType.IsEnum )                       // *** Enum - serialize it as int
            {
                if( options.HasFlag( Options.HumanReadableFormat ) )
                {
                    // Export enum as string + value
                    sb.Append( $"\"{fieldObj} ({(int)fieldObj})\"" );
                } else {
                    // Export enum as number
                    sb.Append( ((int)fieldObj).ToString( CultureInfo.InvariantCulture ) );
                }
            } else if( fieldType.IsArray ) {                    // *** Array
                sb.Append( "[" );

                var array = fieldObj as Array;
                Type arrType = fieldType.GetElementType();  // Get array element type
                for( int i = 0; array != null && i < array.Length; i++ )
                {
                    var val = array.GetValue( i );
                    if( i > 0 ) sb.Append( "," );
                    SerializeField( sb, arrType, val, options );
                }
                sb.Append( "]" );
            } else if( fieldObj is IList array )                // *** List<T>
            {
                sb.Append( "[" );

                Type arrType = fieldType.GetGenericArguments()[0];  // Get List<T> type
                for( int i = 0; array != null && i < array.Count; i++ )
                {
                    var val = array[i];
                    if( i > 0 ) sb.Append( "," );
                    SerializeField( sb, arrType, val, options );
                }
                sb.Append( "]" );

            } else if( fieldObj is IDictionary dict )           // *** IDictionary<T>
            {
                if( options.HasFlag( Options.SerializeDictionaries ) )
                {
                    var args = fieldType.GetGenericArguments();
                    sb.Append( Serialize( dict, args[0], args[1], options ) );
                } else {
                    sb.Append( "{}" );
                    //Debug.LogWarning( $"Not serialized: {fieldType} / {fieldObj}" );
                }


            } else if( fieldType.IsClass                        // *** ScriptableObject - NOT SERIALIZED by default
                && Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), fieldType ) )
            {
                if( options.HasFlag( Options.ReferenceScriptableObjects ) )
                {
                    var so = fieldObj as ScriptableObject;
                    sb.Append( $"\"{so.name}\"" );
                } else {
                    sb.Append( "{}" );
                    Debug.LogWarning( $"Not serialized: {fieldType} / {fieldObj}" );
                }

            } else if( fieldType.IsClassOrStruct() )            // *** Class or Struct
            {
                sb.Append( "{" );
                Serialize( fieldType, fieldObj, sb, options );
                sb.Append( "}" );
            } else {
                sb.Append( fieldObj.ToString() );
            }
        }
        #endregion SerializeField()
        #region [API] SerializeField<T>()
        public static string SerializeField<T>( T fieldObj, Options options = Options.None )
        {
            var sb = new StringBuilder();
            SerializeField<T>( sb, fieldObj, options );
            return sb.ToString();
        }
        public static void SerializeField<T>( StringBuilder sb, T fieldObj, Options options )
        {
            var fieldType = typeof(T);
            SerializeField( sb, fieldType, fieldObj, options );
        }
        #endregion [Util] SerializeField<T>()
        #region [API] Serialize( IDictionary )
        public static string Serialize<T, U>( Dictionary<T, U> data, Options options = Options.None )
        {
            return Serialize( data, typeof( T ), typeof( U ), options );
        }
        public static string Serialize( IDictionary data, Type keyType, Type valType, Options options = Options.None )
        {
            if( data == null ) return null;

            // Convert Dictionary to JSON string
            var sb = new StringBuilder();
            var humanReadable = options.HasFlag( Options.HumanReadableFormat );
            sb.Append( humanReadable ? "{\n" : "{" );

            bool addComma = false;
            foreach( var key in data.Keys )
            {
                if( addComma ) sb.Append( humanReadable ? ",\n" : "," );
                addComma = true;

                // Key
                if( humanReadable ) sb.Append( "    " );
                SerializeField( sb, keyType, key, options );

                // Value
                sb.Append( humanReadable ? ": " : ":" );
                SerializeField( sb, valType, data[key], options );
            }
            sb.Append( humanReadable ? "\n}" : "}" );
            return sb.ToString();
        }
        #endregion Serialize( Dictionary )

        #region [Util] Deserialize()
        public const NumberStyles numberStyle = NumberStyles.Integer | NumberStyles.Float | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        private static object Deserialize( JSONObject root, Type fieldType, bool allowCreationOfScriptableObjects, Options options, object origObject = null )
        {
            if( fieldType == typeof( string ) )                         // *** string
            {
                return root.value;
            } else if( fieldType == typeof( char ) )                    // *** char
            {
                return root.value != null && root.value.Length > 0 ? root.value[0] : (char)0;
#if SUPPORT_OBSCURED_VALUES
            } else if( fieldType == typeof( ObscuredBool ) ) {          // *** ObscuredBool
                ObscuredBool value = root.value == "true" || root.value == "1";
                return value;
            } else if( fieldType == typeof( ObscuredInt ) ) {           // *** ObscuredInt
                if( !int.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out int val ) ) {
                    Debug.LogError( $"Invalid ObscuredInt value: '{root.value}'" );
                    return null;
                }
                ObscuredInt value = val;
                return value;
            } else if( fieldType == typeof( ObscuredLong ) )            // *** ObscuredLong
            {
                if( !long.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out long val ) ) {
                    Debug.LogError( $"Invalid ObscuredLong value: '{root.value}'" );
                    return null;
                }
                ObscuredLong value = val;
                return value;
            } else if( fieldType == typeof( ObscuredFloat ) )           // *** ObscuredFloat
            {
                if( !float.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out float val ) ) {
                    Debug.LogError( $"Invalid ObscuredFloat value: '{root.value}'" );
                    return null;
                }
                ObscuredFloat value = val;
                return value;
            } else if( fieldType == typeof( ObscuredDouble ) )          // *** ObscuredDouble
            {
                if( !double.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out double val ) ) {
                    Debug.LogError( $"Invalid ObscuredDouble value: '{root.value}'" );
                    return null;
                }
                ObscuredDouble value = val;
                return value;
#endif // SUPPORT_OBSCURED_VALUES

            } else if( fieldType == typeof( bool ) )                    // *** bool - serialized as int, to save on space
            {
                return root.value == "1" || root.value == "true" ? true : false;
            } else if( fieldType == typeof( byte ) )                    // *** byte
            {
                if( !byte.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out byte val ) ) {
                    Debug.LogError( $"Invalid byte value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( int ) )                     // *** int
            {
                if( !int.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out int val ) ) {
                    Debug.LogError( $"Invalid int value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( long ) )                    // *** long
            {
                if( !long.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out long val ) ) {
                    Debug.LogError( $"Invalid long value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( float ) )                   // *** float
            {
                if( !float.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out float val ) ) {
                    Debug.LogError( $"Invalid float value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( double ) )                  // *** double
            {
                if( !double.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out double val ) ) {
                    Debug.LogError( $"Invalid double value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType.IsEnum )                               // *** Enum - serialized as int
            {
                if( !int.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out int val ) ) {
                    Debug.LogError( $"Invalid enum (int) value: '{root.value}'" );
                    return null;
                }
                return Enum.ToObject( fieldType, val ); // Convert long to proper enum value
            } else if( fieldType.IsArray )                              // *** Array
            {
                if( root.list == null ) {
                    return null;
                }

                Type arrType = fieldType.GetElementType();  // Get array element type
                var arr = Array.CreateInstance( arrType, root.list.Count );   // Create new array
                for( int i = 0; i < root.list.Count; i++ )
                {
                    var val = Deserialize( root.list[i], arrType, false, options );
                    arr.SetValue( val, i );
                }
                return arr;
            } else if( Reflection.IsGenericList( fieldType ) )     // *** List<>, HashSet<> or Dictionary<,>
            {
                var arguments = fieldType.GetGenericArguments();
                if( arguments.Length == 2 )
                {
                    if( options.HasFlag( Options.SerializeDictionaries ) && fieldType.IsDictionary() )    // *** Dictionary<,>
                    {
                        if( root.objects == null ) return null;

                        var genericListType = typeof( Dictionary<,> );
                        var constructedType = genericListType.MakeGenericType( arguments[0], arguments[1] );

                        var dictionary = Activator.CreateInstance( constructedType ) as IDictionary;
                        foreach( var nameStr in root.objects.Keys )
                        {
                            var jsonObj = new JSONObject() { value = nameStr };
                            var name = Deserialize( jsonObj, arguments[0], false, options );
                            var val = Deserialize( root.objects[nameStr], arguments[1], false, options );
                            dictionary.Add( name, val );
                        }
                        return dictionary;
                    }
                    else {
                        Debug.LogError( $"GFJSON: Unsupported type {fieldType.FullName}" );
                        return null;
                    }
                }
                else if( arguments.Length == 1 )
                {
    #if JSON_HASHSET_SUPPORT
                    if( options.HasFlag( Options.SerializeHashSet ) && fieldType.IsHashSet() )  // *** HashSet<>
                    {
                        if( root.list == null ) return null;

                        var genericListType = typeof( HashSet<> );
                        Type elementType = arguments[0];
                        var constructedHashSetType = genericListType.MakeGenericType( elementType );
                        var hashSet = Activator.CreateInstance( constructedHashSetType );

                        // Since HashSet<T> does not implement an IHashSet<T> interface, and there's no non-generic IHashSet interface
                        // similar to IList, you can't directly cast a HashSet<T> to a common interface for all hash sets as you do with lists.
                        // However, you can still create an instance of HashSet<T> dynamically and use reflection to invoke its Add method.
                        MethodInfo addMethod = constructedHashSetType.GetMethod( "Add" );
                        for( int i = 0; i < root.list.Count; i++ )
                        {
                            var val = Deserialize( root.list[i], elementType, false, options );
                            addMethod.Invoke( hashSet, new[] { val } );
                        }
                        return hashSet;
                    }
                    else
    #endif
                    if( fieldType.IsList() )                                               // *** List<> - always serialized by default
                    {
                        if( root.list == null ) return null;

                        var genericListType = typeof( List<> );
                        Type elementType = arguments[0];  // Get List<T> type
                        var constructedListType = genericListType.MakeGenericType( elementType );
                        var list = Activator.CreateInstance( constructedListType ) as IList;
                        for( int i = 0; i < root.list.Count; i++ )
                        {
                            var val = Deserialize( root.list[i], elementType, false, options );
                            list.Add( val );
                        }
                        return list;

                    } else {
                        Debug.LogError( $"GFJSON: Unsupported type {fieldType.FullName}" );
                        return null;
                    }

                } else {
                    Debug.LogError( $"GFJSON: Unsupported type {fieldType.FullName}" );
                    return null;
                }

            } else if( fieldType.IsClass                                // *** ScriptableObject - NOT SERIALIZED by default
                        && !allowCreationOfScriptableObjects
                        && Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), fieldType ) )
            {
                Debug.LogError( $"Not de-serialized: {fieldType}" );
                return null;
            } else if( fieldType.IsClassOrStruct() )                    // *** Class or Struct
            {
                if( root.objects == null ) {
                    return null;
                }

                var obj = origObject;   // Reuse original object, if set
                // No original object, so instantiate new object
                if( origObject == null ) obj = Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), fieldType )
                    ? (object)ScriptableObject.CreateInstance( fieldType ) // ScriptableObject - should not create with new()
                    : Activator.CreateInstance( fieldType );

                Reflection.WalkAllSerializedFields( fieldType, obj, ( fieldName, fieldInfo, fieldObj ) =>
                {
                    var childRoot = root.objects.ContainsKey(fieldName) ? root.objects[fieldName] : null;
                    if( childRoot == null ) return; // This field doesn't appear in JSON

                    fieldObj = Deserialize( childRoot, fieldInfo.FieldType, false, options, fieldObj );
                    fieldInfo.SetValue( obj, fieldObj );
                }, options );

                return obj;
            } else
            {
                Debug.LogError( $"Unsupported de-serialize type: {fieldType}" );
                return root.value;
            }
        }
        #endregion Deserialize()
    }
}