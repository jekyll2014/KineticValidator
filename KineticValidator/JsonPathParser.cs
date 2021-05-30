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
            Empty,
            Comment,
            Property,
            Object,
            Array,
            Value,
            EndOfObject,
            EndOfArray,
            TokenOrNumber,
            Error
        }

        public class ParsedProperty
        {
            public int StartPosition = -1;
            public int EndPosition = -1;
            public string Path = "";
            public string Name = "";
            public string Value = "";
            public PropertyType Type = PropertyType.Empty;

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
        private static List<ParsedProperty> _pathIndex = new List<ParsedProperty>();

        private static readonly char[] EscapeChars = { '\"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u' };
        private static readonly char[] TokenOrNumber = "-0123456789.truefalsenull".ToCharArray().ToArray();

        private static bool _skipComments;
        private static bool _errorFound;

        public static List<ParsedProperty> ParseJsonPathsStr(string json, out int pos, out bool errorFound, bool skipComments = false)
        {
            _skipComments = skipComments;
            _jsonText = json;
            pos = 0;
            _errorFound = false;

            if (string.IsNullOrEmpty(json))
            {
                errorFound = false;
                return _pathIndex;
            }

            const string currentPath = "";
            _pathIndex = new List<ParsedProperty>();
            while (!_errorFound && pos < _jsonText.Length)
            {
                pos = FindStartOfNextToken(pos, out var foundObjectType);
                if (_errorFound || pos >= _jsonText.Length)
                    break;

                switch (foundObjectType)
                {
                    case PropertyType.Comment:
                        pos = GetComment(pos, currentPath);
                        break;
                    case PropertyType.Object:
                        pos = GetObject(pos, currentPath);
                        break;
                    case PropertyType.EndOfObject:
                        break;
                    default:
                        _errorFound = true;
                        break;
                }

                pos++;
            }

            errorFound = _errorFound;
            return _pathIndex;
        }

        public static List<ParsedProperty> ParseJsonPathsStr(string json, bool skipComments = false)
        {
            ParseJsonPathsStr(json, out var _, out var _, skipComments);

            return _pathIndex;
        }

        private static int FindStartOfNextToken(int pos, out PropertyType foundObjectType)
        {
            foundObjectType = PropertyType.Empty;
            var allowedChars = new List<char> { ' ', '\t', '\r', '\n', ',' };
            var tokenOrNumber = "-0123456789.truefalsenull".ToCharArray().ToList();

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
                    case ']':
                        foundObjectType = PropertyType.EndOfArray;
                        return pos;
                    default:
                    {
                        if (tokenOrNumber.Contains(currentChar))
                        {
                            foundObjectType = PropertyType.TokenOrNumber;
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
                Type = PropertyType.Comment,
                StartPosition = pos,
                Path = currentPath,
                Name = ""
            };
            if (!_skipComments)
            {
                _pathIndex.Add(newElement);
            }

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
                            newElement.Value = _jsonText.Substring(newElement.StartPosition,
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
                                    newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1);
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
                    newElement.Name =
                        _jsonText.Substring(newElement.StartPosition + 1, pos - newElement.StartPosition - 1);
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

                    if (_jsonText[pos] == ',' || _jsonText[pos] == ']') // it's a list of values
                    {
                        pos--;
                        newElement.Value = newElement.Name;
                        newElement.Name = "";
                        newElement.Type = PropertyType.Value;
                        newElement.EndPosition = pos;
                        newElement.Path = currentPath;
                        return pos;
                    }

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

                    if (string.IsNullOrEmpty(currentPath))
                    {
                        currentPath = newElement.Name;
                    }
                    else
                    {
                        currentPath += "." + newElement.Name;
                    }

                    newElement.Path = currentPath;
                    switch (_jsonText[pos])
                    {
                        //it's an object
                        case '{':
                            newElement.Type = PropertyType.Object;
                            newElement.Value = "";
                            newElement.EndPosition = pos = GetObject(pos, currentPath, false);
                            if (_errorFound)
                            {
                                return pos;
                            }

                            return pos;
                        //it's an array
                        case '[':
                            newElement.Type = PropertyType.Array;
                            newElement.Value = "";
                            newElement.EndPosition = pos = GetArray(pos, currentPath);
                            if (_errorFound)
                            {
                                return pos;
                            }
                            return pos;
                        // it's a property
                        default:
                            newElement.Type = PropertyType.Property;
                            newElement.EndPosition = pos;
                            newElement.Value = _jsonText.Substring(valueStartPosition, pos - valueStartPosition + 1)
                                .Trim();
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

        private static int GetTokenOrNumber(int pos, string currentPath)
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
                    if (newValue != "true"
                        && newValue != "false"
                        && newValue != "null"
                        && !IsNumeric(newValue))
                    {
                        _errorFound = true;
                        return pos;
                    }

                    newElement.Value = newValue;
                    newElement.Name = "";
                    newElement.Type = PropertyType.Value;
                    newElement.EndPosition = pos;
                    newElement.Path = currentPath;

                    return pos;
                }

                if (!TokenOrNumber.Contains(currentChar)) // check restricted chars
                {
                    _errorFound = true;
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private static bool IsNumeric(string str)
        {
            return str.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
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
                        GetComment(pos, currentPath);
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
                            var endingChars = new[] { ',', ']', '}', ' ', '\t', '\r', '\n', '/' };
                            var allowedValueChars = "-0123456789.truefalsenull".ToCharArray();

                            for (; pos < _jsonText.Length; pos++)
                            {
                                if (endingChars.Contains(_jsonText[pos]))
                                {
                                    pos--;
                                    return pos;
                                }

                                if (!allowedValueChars.Contains(_jsonText[pos])) // check restricted chars
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
                if (_errorFound)
                {
                    return pos;
                }
				
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
                    case PropertyType.TokenOrNumber:
                        pos = GetTokenOrNumber(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case PropertyType.EndOfArray:
                        return pos;
                    default:
                        _errorFound = true;
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
                newElement = new ParsedProperty
                {
                    StartPosition = pos,
                    Type = PropertyType.Object,
                    Value = "",
                    Name = "",
                    Path = currentPath
                };
                _pathIndex.Add(newElement);
            }

            pos++;

            for (; pos < _jsonText.Length; pos++)
            {
                if (_errorFound)
                {
                    return pos;
                }

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
                        if (save)
                            newElement.EndPosition = pos;
                        return pos;
                    default:
                        _errorFound = true;
                        return pos;
                }
            }

            _errorFound = true;
            return pos;
        }
    }
}
