using UnityEngine;

namespace NightRider.GFJSON
{
    public class StringParser
    {
        private string  data;
        private int     position;
        private int     lastPosition;   // Position in data where last word read by GetKeyword() was

        public bool EOF() => data == null || position >= data.Length;
        public string Data => data;
        public int Length => data != null ? data.Length : 0;
        public int Position => position;
        public int LastPosition => lastPosition;
        
        public StringParser( string str = null, int pos = 0 )
        {
            data = str;
            position = pos;
            lastPosition = 0;
        }
        
        
        public static bool IsJSONChar( char ch ) => char.IsLetterOrDigit( ch ) || ch == '.' || ch == '-' || ch == '_' || ch == '+';

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
        
        
        #region GetJSONKeyword()
        /// <summary>
        /// 
        /// </summary>
        /// <returns>Returns empty string if we reached EOF</returns>
        public string GetJSONKeyword()
        {
            // Skip space
            while( position < data.Length && char.IsWhiteSpace( data[position] ) ) position++;
            if( position >= data.Length ) return "";    // Reached EOF

            lastPosition = position;
            var ch = data[position];
            if( ch == '\"' )                        // *** Open " - read until closing "
            {
                position++;
                int startPos = position;
                while( position < data.Length ) {   // Read until closing "
                    ch = data[position];
                    if( ch == '\\' ) position += 2; // Escape code - skip next 2 chars
                    else if( ch == '\"' ) break;    // Finished
                    else position++;
                }


                if( position < data.Length ) position++;                                // Skip ending "
                else Debug.LogError( "Warning parsing JSON: expecing \" but reached end of file instead!" );
                var text = data.Substring( startPos, position - startPos - 1 );

                return Unescape( text );

            } else if( IsJSONChar( ch ) )           // *** Letter or digit - read until letters, digits, or dot (.). Can start with '-'
            {
                int startPos = position++;
                while( position < data.Length )
                {
                    ch = data[position];
                    if( !IsJSONChar(ch) ) break;
                    position++;
                }
                return data.Substring( startPos, position - startPos );

            } else {                                // *** Single character "'{}[]:,
                position++;
                return data.Substring( position - 1, 1 );
            }
        }
        #endregion GetJSONKeyword()
        
        #region PeekNextJSONKeyword()
        public string PeekNextJSONKeyword()
        {
            int oldPos = position;
            var ret = GetJSONKeyword();
            position = oldPos;
            return ret;
        }
        #endregion PeekNextJSONKeyword()
    }
}
