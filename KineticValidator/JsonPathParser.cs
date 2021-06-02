// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System.Collections.Generic;
using System.Linq;

namespace KineticValidator
{
    internal class JsonPathParser
    {
        public enum PropertyType
        {
            Unknown,
            Comment,
            Property,
            KeywordOrNumberProperty,
            ArrayValue,
            Object,
            Array,
            EndOfObject,
            EndOfArray,
            Error
        }

        public enum ValueType
        {
            Unknown,
            NotProperty,
            String,
            Number,
            Boolean,
            Null,
        }

        public class ParsedProperty
        {
            public int StartPosition = -1;
            public int EndPosition = -1;
            public string Path = "";
            public string Name = "";
            public string Value = "";
            public PropertyType PropertyType = PropertyType.Unknown;
            public ValueType ValueType;

            public int Length
            {
                get
                {
                    if (StartPosition == -1 || EndPosition == -1)
                        return 0;

                    return EndPosition - StartPosition + 1;
                }
            }
        }

        private static string _jsonText = "";
        private static string _rootName = "root";
        private static char _pathDivider = '.';
        private static bool _saveAllValues = false;
        private static List<ParsedProperty> _pathIndex = new List<ParsedProperty>();

        private static readonly char[] EscapeChars = { '\"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u' };
        private static readonly char[] KeywordOrNumberChars = "-0123456789.truefalsnl".ToCharArray();
        private static readonly string[] Keywords = { "true", "false", "null" };

        private static bool _errorFound;

        public static IEnumerable<ParsedProperty> ParseJsonToPathList(string json, out int endPosition, out bool errorFound, string rootName = "root", char pathDivider = '.', bool saveAllValues = true)
        {
            _rootName = rootName;
            _pathDivider = pathDivider;
            _jsonText = json;
            _saveAllValues = saveAllValues;
            endPosition = 0;
            _errorFound = false;
            _pathIndex = new List<ParsedProperty>();

            if (string.IsNullOrEmpty(json))
            {
                errorFound = _errorFound;
                return _pathIndex;
            }

            var currentPath = _rootName;
            while (!_errorFound && endPosition < _jsonText.Length)
            {
                endPosition = FindStartOfNextToken(endPosition, out var foundObjectType);
                if (_errorFound || endPosition >= _jsonText.Length)
                    break;

                switch (foundObjectType)
                {
                    case PropertyType.Property:
                        endPosition = GetPropertyName(endPosition, currentPath);
                        break;
                    case PropertyType.Comment:
                        endPosition = GetComment(endPosition, currentPath);
                        break;
                    case PropertyType.Object:
                        endPosition = GetObject(endPosition, currentPath);
                        break;
                    case PropertyType.EndOfObject:
                        break;
                    case PropertyType.Array:
                        endPosition = GetArray(endPosition, currentPath);
                        break;
                    case PropertyType.EndOfArray:
                        break;
                    default:
                        _errorFound = true;
                        break;
                }

                endPosition++;
            }

            errorFound = _errorFound;
            return _pathIndex;
        }

        public static IEnumerable<ParsedProperty> ParseJsonToPathList(string json)
        {
            return ParseJsonToPathList(json, out var _, out var _);
        }

        private static int FindStartOfNextToken(int pos, out PropertyType foundObjectType)
        {
            foundObjectType = new PropertyType();
            var allowedChars = new List<char> { ' ', '\t', '\r', '\n', ',' };

            for (; pos < _jsonText.Length; pos++)
            {
                var currentChar = _jsonText[pos];
                switch (currentChar)
                {
                    case '/':
                        foundObjectType = PropertyType.Comment;
                        return pos;
                    case '\"':
                        foundObjectType = PropertyType.Property;
                        return pos;
                    case '{':
                        foundObjectType = PropertyType.Object;
                        return pos;
                    case '}':
                        foundObjectType = PropertyType.EndOfObject;
                        return pos;
                    case '[':
                        foundObjectType = PropertyType.Array;
                        return pos;
                    case ']':
                        foundObjectType = PropertyType.EndOfArray;
                        return pos;
                    default:
                    {
                        if (KeywordOrNumberChars.Contains(currentChar))
                        {
                            foundObjectType = PropertyType.KeywordOrNumberProperty;
                            return pos;
                        }

                        if (!allowedChars.Contains(currentChar))
                        {
                            foundObjectType = PropertyType.Error;
                            _errorFound = true;
                            return pos;
                        }

                        break;
                    }
                }
            }

            return pos;
        }

        private static int GetComment(int pos, string currentPath)
        {
            var newElement = new ParsedProperty
            {
                PropertyType = PropertyType.Comment,
                StartPosition = pos,
                Path = currentPath,
                ValueType = ValueType.NotProperty
            };
            _pathIndex.Add(newElement);

            pos++;

            if (pos >= _jsonText.Length)
            {
                _errorFound = true;
                return pos;
            }

            switch (_jsonText[pos])
            {
                //single line comment
                case '/':
                {
                    pos++;
                    if (pos >= _jsonText.Length)
                    {
                        _errorFound = true;
                        return pos;
                    }

                    for (; pos < _jsonText.Length; pos++)
                    {
                        if (_jsonText[pos] == '\r' || _jsonText[pos] == '\n') //end of comment
                        {
                            pos--;
                            newElement.EndPosition = pos;
                            newElement.Value = _jsonText.Substring(newElement.StartPosition + 2,
                                newElement.EndPosition - newElement.StartPosition + 1);

                            return pos;
                        }
                    }

                    return pos;
                }
                //multi line comment
                case '*':
                {
                    pos++;
                    if (pos >= _jsonText.Length)
                    {
                        _errorFound = true;
                        return pos;
                    }

                    for (; pos < _jsonText.Length; pos++)
                    {
                        if (_jsonText[pos] == '*') // possible end of comment
                        {
                            pos++;
                            if (pos >= _jsonText.Length)
                            {
                                _errorFound = true;
                                return pos;
                            }

                            if (_jsonText[pos] == '/')
                            {
                                newElement.EndPosition = pos;
                                newElement.Value = _jsonText.Substring(
                                    newElement.StartPosition + 2,
                                    newElement.EndPosition - newElement.StartPosition - 1);

                                return pos;
                            }

                            pos--;
                        }
                    }

                    break;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetPropertyName(int pos, string currentPath)
        {
            var incorrectChars = new List<char> { '\r', '\n' };

            var newElement = new ParsedProperty
            {
                StartPosition = pos
            };
            _pathIndex.Add(newElement);

            pos++;

            for (; pos < _jsonText.Length; pos++) // searching for property name end
            {
                var currentChar = _jsonText[pos];

                if (currentChar == '\\') //skip escape chars
                {
                    pos++;
                    if (pos >= _jsonText.Length)
                    {
                        _errorFound = true;
                        return pos;
                    }

                    if (EscapeChars.Contains(_jsonText[pos])) // if \u0000
                    {
                        if (_jsonText[pos] == 'u')
                            pos += 4;
                    }
                    else
                    {
                        _errorFound = true;
                        return pos;
                    }
                }
                else if (currentChar == '\"') // end of property name found
                {
                    var newName = _jsonText.Substring(newElement.StartPosition, pos - newElement.StartPosition + 1);
                    pos++;

                    if (pos >= _jsonText.Length)
                    {
                        _errorFound = true;
                        return pos;
                    }

                    pos = GetPropertyDivider(pos, currentPath);

                    if (_errorFound)
                    {
                        return pos;
                    }

                    if (_jsonText[pos] == ',' || _jsonText[pos] == ']') // it's an array of values
                    {
                        pos--;
                        newElement.Value = newName;
                        newElement.PropertyType = PropertyType.ArrayValue;
                        newElement.EndPosition = pos;
                        newElement.Path = currentPath;
                        newElement.ValueType = GetVariableType(newName);
                        return pos;
                    }

                    newElement.Name = newName.Trim('\"');
                    pos++;
                    if (pos >= _jsonText.Length)
                    {
                        _errorFound = true;
                        return pos;
                    }

                    var valueStartPosition = pos;
                    pos = GetPropertyValue(pos, currentPath);
                    if (_errorFound)
                    {
                        return pos;
                    }

                    currentPath += _pathDivider + newElement.Name;
                    newElement.Path = currentPath;
                    switch (_jsonText[pos])
                    {
                        //it's an object
                        case '{':
                            newElement.PropertyType = PropertyType.Object;
                            newElement.EndPosition = pos = GetObject(pos, currentPath, false);
                            newElement.ValueType = ValueType.NotProperty;

                            if (_saveAllValues)
                            {
                                newElement.Value = TrimObjectValue(_jsonText.Substring(newElement.StartPosition,
                                newElement.EndPosition - newElement.StartPosition + 1));
                            }

                            return pos;
                        //it's an array
                        case '[':
                            newElement.PropertyType = PropertyType.Array;
                            newElement.EndPosition = pos = GetArray(pos, currentPath);
                            newElement.ValueType = ValueType.NotProperty;

                            if (_saveAllValues)
                            {
                                newElement.Value = TrimArrayValue(_jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1));
                            }

                            return pos;
                        // it's a property
                        default:
                            newElement.PropertyType = PropertyType.Property;
                            newElement.EndPosition = pos;
                            var newValue = _jsonText.Substring(valueStartPosition, pos - valueStartPosition + 1)
                                   .Trim();
                            newElement.ValueType = GetVariableType(newValue);
                            newElement.Value = newElement.ValueType == ValueType.String ? newValue.Trim('\"') : newValue;
                            return pos;
                    }
                }
                else if (incorrectChars.Contains(currentChar)) // check restricted chars
                {
                    _errorFound = true;
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetKeywordOrNumber(int pos, string currentPath, bool isArray)
        {
            var newElement = new ParsedProperty
            {
                StartPosition = pos
            };
            _pathIndex.Add(newElement);

            var endingChars = new List<char> { ',', ']', '\r', '\n', '/' };

            for (; pos < _jsonText.Length; pos++) // searching for property name end
            {
                var currentChar = _jsonText[pos];
                // end of property name found
                if (endingChars.Contains(currentChar))
                {
                    pos--;
                    var newValue = _jsonText.Substring(newElement.StartPosition, pos - newElement.StartPosition + 1)
                           .Trim();

                    if (!Keywords.Contains(newValue)
                        && !IsNumeric(newValue))
                    {
                        _errorFound = true;
                        return pos;
                    }

                    newElement.Value = newValue;
                    newElement.PropertyType = isArray ? PropertyType.ArrayValue : PropertyType.KeywordOrNumberProperty;
                    newElement.EndPosition = pos;
                    newElement.Path = currentPath;
                    newElement.ValueType = GetVariableType(newValue);

                    return pos;
                }

                if (!KeywordOrNumberChars.Contains(currentChar)) // check restricted chars
                {
                    _errorFound = true;
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetPropertyDivider(int pos, string currentPath)
        {
            var allowedChars = new List<char> { ' ', '\t', '\r', '\n' };
            for (; pos < _jsonText.Length; pos++)
            {
                switch (_jsonText[pos])
                {
                    case ':':
                    case ']':
                    case ',':
                        return pos;
                    case '/':
                        pos = GetComment(pos, currentPath);
                        break;
                    default:
                        if (!allowedChars.Contains(_jsonText[pos]))
                        {
                            _errorFound = true;
                            return pos;
                        }
                        break;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetPropertyValue(int pos, string currentPath)
        {
            var allowedChars = new[] { ' ', '\t', '\r', '\n' };
            for (; pos < _jsonText.Length; pos++)
            {
                switch (_jsonText[pos])
                {
                    case '[':
                    // it's a start of array
                    case '{':
                        return pos;
                    case '/':
                        //it's a comment
                        pos = GetComment(pos, currentPath);
                        break;
                    //it's a start of value string 
                    case '\"':
                    {
                        pos++;
                        var incorrectChars = new List<char> { '\r', '\n' }; // to be added

                        for (; pos < _jsonText.Length; pos++)
                        {
                            if (_jsonText[pos] == '\\') //skip escape chars
                            {
                                pos++;
                                if (pos >= _jsonText.Length)
                                {
                                    _errorFound = true;
                                    return pos;
                                }

                                if (EscapeChars.Contains(_jsonText[pos])) // if \u0000
                                {
                                    if (_jsonText[pos] == 'u')
                                        pos += 4;
                                }
                                else
                                {
                                    _errorFound = true;
                                    return pos;
                                }
                            }
                            else if (_jsonText[pos] == '\"')
                            {
                                return pos;
                            }
                            else if (incorrectChars.Contains(_jsonText[pos])) // check restricted chars
                            {
                                _errorFound = true;
                                return pos;
                            }
                        }

                        _errorFound = true;
                        return pos;
                    }
                    default:
                        if (!allowedChars.Contains(_jsonText[pos])) // it's a property non-string value
                        {
                            // ??? check this
                            var endingChars = new[] { ',', ']', '}', ' ', '\t', '\r', '\n', '/' };
                            for (; pos < _jsonText.Length; pos++)
                            {
                                if (endingChars.Contains(_jsonText[pos]))
                                {
                                    pos--;
                                    return pos;
                                }

                                if (!KeywordOrNumberChars.Contains(_jsonText[pos])) // check restricted chars
                                {
                                    _errorFound = true;
                                    return pos;
                                }
                            }
                        }
                        break;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetArray(int pos, string currentPath)
        {
            pos++;
            var arrayIndex = 0;
            for (; pos < _jsonText.Length; pos++)
            {
                pos = FindStartOfNextToken(pos, out var foundObjectType);
                if (_errorFound)
                {
                    return pos;
                }

                switch (foundObjectType)
                {
                    case PropertyType.Comment:
                        pos = GetComment(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case PropertyType.Property:
                        pos = GetPropertyName(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case PropertyType.Object:
                        pos = GetObject(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case PropertyType.KeywordOrNumberProperty:
                        pos = GetKeywordOrNumber(pos, currentPath + "[" + arrayIndex + "]", true);
                        arrayIndex++;
                        break;
                    case PropertyType.EndOfArray:
                        return pos;
                    default:
                        _errorFound = true;
                        return pos;
                }

                if (_errorFound)
                {
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static int GetObject(int pos, string currentPath, bool save = true)
        {
            var newElement = new ParsedProperty();
            if (save)
            {
                newElement.StartPosition = pos;
                newElement.PropertyType = PropertyType.Object;
                newElement.Path = currentPath;
                newElement.ValueType = ValueType.NotProperty;
                _pathIndex.Add(newElement);
            }

            pos++;
            for (; pos < _jsonText.Length; pos++)
            {
                pos = FindStartOfNextToken(pos, out var foundObjectType);
                if (_errorFound)
                {
                    return pos;
                }

                switch (foundObjectType)
                {
                    case PropertyType.Comment:
                        pos = GetComment(pos, currentPath);
                        break;
                    case PropertyType.Property:
                        pos = GetPropertyName(pos, currentPath);
                        break;
                    case PropertyType.Object:
                        pos = GetObject(pos, currentPath);
                        break;
                    case PropertyType.EndOfObject:
                        if (!_errorFound && save)
                        {
                            newElement.EndPosition = pos;
                            if (_saveAllValues)
                            {
                                newElement.Value = TrimObjectValue(_jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1));
                            }
                        }
                        return pos;
                    default:
                        _errorFound = true;
                        return pos;
                }

                if (_errorFound)
                {
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static bool IsNumeric(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }

            return str.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
        }

        public static ValueType GetVariableType(string text)
        {
            var type = ValueType.Unknown;

            if (string.IsNullOrEmpty(text))
            {
                type = ValueType.Unknown;
            }
            else if (IsNumeric(text))
            {
                type = ValueType.Number;
            }
            else if (text == "null")
            {
                type = ValueType.Null;
            }
            else if (text == "true" || text == "false")
            {
                type = ValueType.Boolean;
            }
            else if (text.Length > 1 && text[0] == ('\"') && text[text.Length - 1] == ('\"'))
            {
                type = ValueType.String;
            }
            return type;
        }

        public static string TrimObjectValue(string objectText)
        {
            if (string.IsNullOrEmpty(objectText))
            {
                return objectText;
            }

            var startPosition = objectText.IndexOf('{');
            var endPosition = objectText.LastIndexOf('}');

            if (startPosition < 0 || endPosition <= 0 || endPosition <= startPosition)
            {
                return objectText;
            }

            return objectText.Substring(startPosition + 1, endPosition - startPosition - 1).Trim();
        }

        public static string TrimArrayValue(string arrayText)
        {
            if (string.IsNullOrEmpty(arrayText))
            {
                return arrayText;
            }

            var startPosition = arrayText.IndexOf('[');
            var endPosition = arrayText.LastIndexOf(']');

            if (startPosition < 0 || endPosition <= 0 || endPosition <= startPosition)
            {
                return arrayText;
            }

            return arrayText.Substring(startPosition + 1, endPosition - startPosition - 1).Trim();
        }
    }
}
