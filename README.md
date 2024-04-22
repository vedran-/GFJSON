# GFJSON

GFJSON - a simple, fast, and flexible JSON serializer and deserializer, specialized for Unity.


# Features
Anything which Unity serializes can be serialized with GFJSON.
On top of that, it can serialize Dictionary and ScriptableObject, if you provide the right options.


# Attributes
You can add attributes to your fields to control how they are serialized.

### [JSONName]
You can use this attribute to specify a different name for a field in JSON.

### [JSONIgnore]
You can use this attribute to ignore a field in JSON serialization.


# API Reference

### DeserializeToNewObject<T>( string json, Options options )
Deserialize JSON string to new object.

### DeserializeInto<T>( string jsonStr, T obj, Options options )
Deserializes JSON string into an existing object.
It will reuse object (and its child objects) as much as possible when serializing again.

### Serialize<T>( T obj, Options options )
Serializes any (Unity serializable) class or struct to a JSON string.
- It can serialize ScriptableObject, if you provide Options.ReferenceScriptableObjects flag.
- It can serialize Dictionary, if you provide Options.SerializeDictionaries flag.

### SerializeField<T>( T fieldObj, Options options )
### SerializeField( Type fieldType, object fieldObj, Options options )
Serializes any (Unity serializable) field to a JSON string.

### Serialize<T, U>( Dictionary<T, U> data, Options options )
Serializes a Dictionary to JSON string directly.


### Parse( string json )
Deserialize JSON string to JSONObject hierarchy.


# Options
Options is an enum that you can pass to serialization and deserialization methods.

- **None** - Default options.
- **IgnoreJSONName** - Ignore [JSONName] attribute.
- **HumanReadableFormat** - Export references and enums as human-readable names.
- **ReferenceScriptableObjects** - If set, it will also reference ScriptableObjects.
- **SerializeDictionaries** - If set, it will also serialize Dictionary<,>.






