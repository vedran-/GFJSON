using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace NightRider.GFJSON
{
    public static class Reflection
    {
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
    }
}
