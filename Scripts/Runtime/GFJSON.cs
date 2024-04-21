//#define SUPPORT_OBSCURED_VALUES

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
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
        }

        #region class Object
        [Serializable]
        public class Object
        {
            // NOTE: Only one of these 3 variables should have value, the rest should be null
            public string                       value = null;       // For direct values (e.g. int, string, bool, ...)
            public Dictionary<string, Object>   objects = null;     // For classes and structs
            public List<Object>                 list = null;        // For arrays and lists

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
        #endregion class Object

        #region [API] Parse()
        public static Object Parse( string json ) => Parse( new StringParser( json ) );
        public static Object Parse( StringParser sp )
        {
            if( sp.EOF() ) return null;

            var obj = new Object();

            var keyword = sp.GetJSONKeyword();
            if( keyword == "{" )            // *** Parse object
            {
                obj.objects = new Dictionary<string, Object>();

                keyword = sp.GetJSONKeyword();
                while( !sp.EOF() )
                {
                    if( keyword == "}" ) break; // Properly finished with list

                    var name = keyword;
                    keyword = sp.GetJSONKeyword();
                    if( keyword != ":" ) {
                        Log.Error( $"Expecting ':' parsing JSON at position {sp.Position}, but got '{keyword}' instead (at '{name}')!" );
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
                        Log.Error( $"Invalid keyword '{keyword}' at position {sp.Position} in JSON object: expecting , or }}" );
                    }
                }

            } else if( keyword == "[" )     // *** Parse array
            {
                obj.list = new List<Object>();

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
                        Log.Error( $"Invalid keyword '{keyword}' at position {sp.Position} in JSON array: expecting , or ]" );
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
        #region [API] Serialize<T>()
        /// <summary>
        /// Serializes any (Unity serializable) object to JSON string
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static string Serialize<T>( T obj, Options options = Options.None )
        {
            var sb = new StringBuilder();
            sb.Append( "{" );
            Serialize( typeof(T), obj, sb, options );
            sb.Append( "}" );
            return sb.ToString();
        }
        #endregion Serialize()

        #region [API] DeserializeToNewObject<T>()
        /// <summary>
        /// Deserializes JSON string into new object.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="jsonStr"></param>
        /// <param name="options">Deserialization options</param>
        /// <returns></returns>
        public static T DeserializeToNewObject<T>( string jsonStr, Options options = Options.None )
        {
            var json = Parse( jsonStr );
            if( json == null ) {
                Log.Error( "Error parsing JSON string!" );
                return default(T);
            }
            return (T)Deserialize( json, typeof(T), true, options );
        }

        public static T DeserializeToNewObject<T>( GFJSON.Object json, Options options = Options.None )
        {
            if( json == null )
            {
                Log.Error( "DeserializeToNewObject: JSON object is null!" );
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
        /// <param name="options">Deserialization options</param>
        /// <returns></returns>
        public static bool DeserializeInto<T>( string jsonStr, T obj, Options options = Options.None ) where T : class
        {
            var json = Parse( jsonStr );
            if( json == null ) {
                Log.Error( "Error parsing JSON string!" );
                return false;
            }

            WalkAllSerializedFields( typeof(T), obj, ( fieldName, fieldInfo, fieldObj ) =>
            {
                var childRoot = json.objects.ContainsKey(fieldName) ? json.objects[fieldName] : null;
                if( childRoot == null ) return; // This field doesn't appear in JSON, so just skip it

                fieldObj = Deserialize( childRoot, fieldInfo.FieldType, false, options, fieldObj );
                fieldInfo.SetValue( obj, fieldObj );
            }, options );

            return true;
        }
        #endregion DeserializeInto()

        #region class JavaSchemeOptions
        public class JavaSchemeOptions
        {
            public Options  options = Options.None;

            /// <summary>
            /// If true, it will allow classes-within-classes. If false, then it will define all the classes as subclass of the root class.
            /// </summary>
            public bool     allowNestedClasses = false;

            public string   classDefinitionPrefix = "public ";
            public string   classDefinitionSuffix = "\n{\n";

            public string   subclassDefinitionPrefix = "public ";
            public string   subclassDefinitionSuffix = "\n{\n";
        }
        #endregion class JavaSchemeOptions

        #region [API] ExportJavaScheme()
        public static string ExportJavaScheme( Type classType, JavaSchemeOptions options, int indent = 0 )
        {
            var sb = new StringBuilder();
            ExportJavaScheme( sb, classType, options, false, indent );
            return sb.ToString();
        }
        #endregion ExportJavaScheme()
        #region [Util] ExportJavaScheme()
        static void ExportJavaScheme( StringBuilder sb, Type classType, JavaSchemeOptions options, bool isSubclass, int indent, List<Type> subclasses = null )
        {
            var indentStr = "".PadLeft( 4 * indent );

            // Class definition
            sb.Append( Indent( isSubclass ? options.subclassDefinitionPrefix : options.classDefinitionPrefix, indentStr ) );
            sb.Append( $"class {classType.Name}" );
            sb.Append( isSubclass ? options.subclassDefinitionSuffix : options.classDefinitionSuffix );
            sb.AppendLine();

            var indentSubStr = "".PadLeft( 4 * (indent+1) );

            sb.AppendLine( Indent( "private static final long serialVersionUID = 1L;", indentSubStr ) + "\n" );

            // Only instance which created list of subclasses should handle it
            var handleSubclasses = subclasses == null;
            if( handleSubclasses ) subclasses = new List<Type>();

            WalkAllSerializedFields( classType, null, ( fieldName, fieldInfo, fieldObj ) =>
            {
                // Skip fields with 'JavaSchemaSkip' attribute
                if( Attribute.IsDefined( fieldInfo, typeof( JavaSchemeSkipAttribute ) ) ) return;

                // Get type
                var type = GetJavaType( fieldInfo.FieldType, options.options, subclasses );
                if( type == null ) return;  // Not a serialized field

                // Variable & JSON name
                var name = fieldInfo.Name;
                var jsonName = fieldName;

                sb.AppendLine( $"{indentSubStr}@XmlElement(name = \"{jsonName}\")" );
                sb.AppendLine( $"{indentSubStr}private {type} {name};\n" );
            }, options.options, skipNullValue: false );

            // Export all subclasses
            if( handleSubclasses ) 
            {
                // NOTE: subclasses list can grow with each call to ExportJavaScheme, as new classes are being added
                for( int idx = 0; idx < subclasses.Count; idx++ )
                {
                    ExportJavaScheme( sb, subclasses[idx], options, true, indent + 1, options.allowNestedClasses ? null : subclasses );
                }
            }

            // Class ending
            sb.AppendLine( $"{indentStr}}}" );
        }
        #endregion ExportJavaScheme()

        #region [Util] GetJavaType()
        static string GetJavaType( Type type, Options options, List<Type> allTypes )
        {
    #if SUPPORT_OBSCURED_VALUES
            if( type == typeof( bool ) || type == typeof( ObscuredBool ) ) return "Boolean";
            if( type == typeof( int ) || type == typeof( ObscuredInt ) ) return "Integer";
            if( type == typeof( long ) || type == typeof( ObscuredLong ) ) return "Long";
            if( type == typeof( float ) || type == typeof( ObscuredFloat ) ) return "Float";
            if( type == typeof( double ) || type == typeof( ObscuredDouble ) ) return "Double";
            if( type == typeof( string ) || type == typeof( ObscuredString ) ) return "String";
    #endif
            if( type == typeof( byte ) ) return "Byte";
            if( type.IsEnum ) return "Long";

            // Array
            if( type.IsArray ) {
                var baseType = type.GetElementType();
                GetJavaType( baseType, options, allTypes );   // This will actually just add baseType to allTypes, if needed
                return $"{GetJavaType( baseType, options, allTypes )}[]";
            }

            // List
            if( type.IsGenericType && type.GetGenericTypeDefinition() == typeof( List<> ) ) {
                var baseType = type.GetGenericArguments().Single();
                GetJavaType( baseType, options, allTypes );   // This will actually just add baseType to allTypes, if needed
                return $"List<{ GetJavaType( baseType, options, allTypes ) }>";
            }

            // ScriptableObject - NOT SERIALIZED by default
            if( type.IsClass && Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), type ) ) {
                if( options.HasFlag( Options.ReferenceScriptableObjects ) ) {
                    if( !allTypes.Contains( type ) ) allTypes.Add( type );
                    return type.Name;
                } else return null;
            }

            // Class or Struct
            if( type.IsClassOrStruct() ) {
                if( !allTypes.Contains( type ) ) allTypes.Add( type );
                return type.Name;
            }

            Log.Error( $"Not found type: '<b>{type}</b>'!" );
            return "<UNSUPPORTED>" + type.Name;
        }
        #endregion GetJavaType()

        #region [Util] Escape()
        public static string Escape( string str )
        {
            return str.Replace( "\\", @"\\" )
                .Replace( "\n", @"\n" )
                .Replace( "\r", @"\r" )
                .Replace( "\"", @"\""" )
                .Replace( "\t", @"\t" )
                .Replace( "\f", @"\f" )
                .Replace( "\b", @"\b" );
        }
        #endregion Escape()
        #region [Util] Unescape()
        public static string Unescape( string str )
        {
            return str.Replace( @"\\", "\\" )
                .Replace( @"\n", "\n" )
                .Replace( @"\r", "\r" )
                .Replace( @"\""", "\"" )
                .Replace( @"\t", "\t" )
                .Replace( @"\f", "\f" )
                .Replace( @"\b", "\b" );
        }
        #endregion Unescape()
        #region [Util] Indent()
        static string Indent( string str, string indentString )
        {
            return string.Join( "\n", str.Split( '\n' ).Select( line => indentString + line ) );
        }
        #endregion Indent()

        #region [Util] Type.IsClassOrStruct()
        public static bool IsClassOrStruct( this Type type )
        {
            return (type.IsClass || (type.IsValueType && !type.IsPrimitive)) && !type.IsEnum;
        }
        #endregion IsClassOrStruct()

        #region [Util] WalkAllSerializedFields()
        internal static void WalkAllSerializedFields( Type type, object obj, Action<string, FieldInfo, object> onField, Options options, bool skipNullValue = true )
        {
            if( skipNullValue && obj == null ) return;

            foreach( var field in type.GetFields( allFields ) )
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
                if( !options.HasFlag(Options.IgnoreJSONName) )
                {
                    var jsonName = field.GetCustomAttributes( typeof(JSONNameAttribute), false ) as JSONNameAttribute[];
                    if( jsonName.Length > 0 ) {         // Use GFJSON name
                        fieldName = jsonName[0].Name;
                    } else {
                        // Check for DataMember
                        var dataMemberName = field.GetCustomAttributes( typeof( DataMemberAttribute ), false ) as DataMemberAttribute[];
                        if( dataMemberName.Length > 0 ) {
                            fieldName = dataMemberName[0].Name;
                        } else fieldName = field.Name;
                    }

                } else fieldName = field.Name;

                onField( fieldName, field, obj != null ? field.GetValue(obj) : null );
            }
        }
        #endregion WalkAllSerializedFields()

        #region [Util] Serialize()
        private const BindingFlags allFields = BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic;
        private static void Serialize( Type type, object obj, StringBuilder sb, Options options )
        {
            var appendComma = false;

            // Serialize all the fields inside object
            WalkAllSerializedFields( type, obj, (fieldName, field, value) =>
            {
                if( appendComma ) sb.Append( "," );
                appendComma = true;

                // Append field name
                sb.Append( $"\"{fieldName}\":" );

                SerializeField( sb, field.FieldType, obj != null ? field.GetValue( obj ) : null, options );
            }, options );
        }
        #endregion Serialize()
        #region [Util] SerializeField()
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
                Log.Error( $"No fieldType for obj {fieldObj}" );
                return;
            }

            if( fieldType == typeof(string) )                   // *** String
            {
                sb.Append($"\"{Escape(fieldObj as string)}\"");
            } else if( fieldType == typeof(char) )              // *** Char
            {
                sb.Append( $"\"{Escape(((char)fieldObj).ToString())}\"" );
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
    #endif
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
                if( options.HasFlag(Options.HumanReadableFormat) )
                {
                    // Export enum as string + value
                    sb.Append( $"\"{fieldObj} ({(int)fieldObj})\"" );
                } else {
                    // Export enum as number
                    sb.Append( ((int)fieldObj).ToString(CultureInfo.InvariantCulture) );
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
            } else if( fieldObj is IList )                      // *** List<T>
            {
                sb.Append( "[" );

                var array = fieldObj as IList;
                Type arrType = fieldType.GetGenericArguments()[0];  // Get List<T> type
                for( int i = 0; array != null && i < array.Count; i++ )
                {
                    var val = array[i];
                    if( i > 0 ) sb.Append( "," );
                    SerializeField( sb, arrType, val, options );
                }
                sb.Append( "]" );

            } else if( fieldType.IsClass                        // *** ScriptableObject - NOT SERIALIZED by default
                && Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), fieldType ) )
            {
                if( options.HasFlag( Options.ReferenceScriptableObjects ) )
                {
                    var so = fieldObj as ScriptableObject;
                    sb.Append( $"\"{so.name}\"" );
                } else {
                    sb.Append( "{}" );
                    Log.Error( $"Not serialized: {fieldType} / {fieldObj}" );
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
        #region [Util] SerializeField<T>()
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
        #region [Util] Serialize( Dictionary )
        public static string Serialize<T, U>( Dictionary<T, U> data, Options options = Options.None )
        {
            if( data == null ) return null;

            // Convert Dictionary to JSON string
            var sb = new StringBuilder();
            sb.Append( "{" );

            bool addComma = false;
            foreach( var key in data.Keys )
            {
                if( addComma ) sb.Append( "," );
                addComma = true;
                var val = data[key];
                var type = val.GetType();

                sb.Append( $"\"{key}\":" );

                SerializeField( sb, type, val, options );
            }
            sb.Append( "}" );
            return sb.ToString();
        }
        #endregion Serialize( Dictionary )

        #region [Util] Deserialize()
        public const NumberStyles numberStyle = NumberStyles.Integer | NumberStyles.Float | NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign;
        private static object Deserialize( Object root, Type fieldType, bool allowCreationOfScriptableObjects, Options options, object origObject = null )
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
                    Log.Error( $"Invalid ObscuredInt value: '{root.value}'" );
                    return null;
                }
                ObscuredInt value = val;
                return value;
            } else if( fieldType == typeof( ObscuredLong ) )            // *** ObscuredLong
            {
                if( !long.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out long val ) ) {
                    Log.Error( $"Invalid ObscuredLong value: '{root.value}'" );
                    return null;
                }
                ObscuredLong value = val;
                return value;
            } else if( fieldType == typeof( ObscuredFloat ) )           // *** ObscuredFloat
            {
                if( !float.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out float val ) ) {
                    Log.Error( $"Invalid ObscuredFloat value: '{root.value}'" );
                    return null;
                }
                ObscuredFloat value = val;
                return value;
            } else if( fieldType == typeof( ObscuredDouble ) )          // *** ObscuredDouble
            {
                if( !double.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out double val ) ) {
                    Log.Error( $"Invalid ObscuredDouble value: '{root.value}'" );
                    return null;
                }
                ObscuredDouble value = val;
                return value;
    #endif
            } else if( fieldType == typeof( bool ) )                    // *** bool - serialized as int, to save on space
            {
                return root.value == "1" || root.value == "true" ? true : false;
            } else if( fieldType == typeof( byte ) )                    // *** byte
            {
                if( !byte.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out byte val ) ) {
                    Log.Error( $"Invalid byte value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( int ) )                     // *** int
            {
                if( !int.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out int val ) ) {
                    Log.Error( $"Invalid int value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( long ) )                    // *** long
            {
                if( !long.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out long val ) ) {
                    Log.Error( $"Invalid long value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( float ) )                   // *** float
            {
                if( !float.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out float val ) ) {
                    Log.Error( $"Invalid float value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType == typeof( double ) )                  // *** double
            {
                if( !double.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out double val ) ) {
                    Log.Error( $"Invalid double value: '{root.value}'" );
                    return null;
                }
                return val;
            } else if( fieldType.IsEnum )                               // *** Enum - serialized as int
            {
                if( !int.TryParse( root.value, numberStyle, CultureInfo.InvariantCulture, out int val ) ) {
                    Log.Error( $"Invalid enum (int) value: '{root.value}'" );
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
            } else if( Reflection.IsGenericList( fieldType ) )     // *** List
            {
                if( root.list == null ) {
                    return null;
                }

                Type listType = fieldType.GetGenericArguments()[0];  // Get List<T> type
                var genericListType = typeof(List<>);
                var constructedListType = genericListType.MakeGenericType(listType);
                var list = Activator.CreateInstance(constructedListType) as IList;
                for( int i = 0; i < root.list.Count; i++ )
                {
                    var val = Deserialize( root.list[i], listType, false, options );
                    list.Add( val );
                }
                return list;

            } else if( fieldType.IsClass                                // *** ScriptableObject - NOT SERIALIZED by default
                        && !allowCreationOfScriptableObjects
                        && Reflection.IsSubclassOfRawGeneric( typeof( ScriptableObject ), fieldType ) )
            {
                Log.Error( $"Not de-serialized: {fieldType}" );
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

                WalkAllSerializedFields( fieldType, obj, ( fieldName, fieldInfo, fieldObj ) =>
                {
                    var childRoot = root.objects.ContainsKey(fieldName) ? root.objects[fieldName] : null;
                    if( childRoot == null ) return; // This field doesn't appear in JSON

                    fieldObj = Deserialize( childRoot, fieldInfo.FieldType, false, options, fieldObj );
                    fieldInfo.SetValue( obj, fieldObj );
                }, options );

                return obj;
            } else
            {
                Log.Error( $"Unsupported de-serialize type: {fieldType}" );
                return root.value;
            }
        }
        #endregion Deserialize()
    }
}