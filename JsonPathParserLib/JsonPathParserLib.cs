using System.Collections.Generic;
using System.Linq;

namespace JsonPathParserLib
{
    public enum PropertyType
    {
        Unknown,
        Comment,
        Property,
        KeywordOrNumberProperty,
        ArrayValue,
        Object,
        EndOfObject,
        Array,
        EndOfArray,
        Error
    }

    public enum JsonValueType
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
        public JsonValueType ValueType;

        public int Length
        {
            get
            {
                if (StartPosition == -1 || EndPosition == -1)
                    return 0;

                return EndPosition - StartPosition + 1;
            }
        }

        public override string ToString()
        {
            return Path;
        }
    }

    public class JsonPathParser
    {
        private string _jsonText = "";
        private string _rootName = "root";
        private char _jsonPathDivider = '.';
        private bool _saveAllValues ;
        private bool _trimComplexValues ;
        private bool _fastSearch ;

        private List<ParsedProperty> _pathIndex;

        private readonly char[] _escapeChars = new char[] { '\"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u' };
        private readonly char[] _allowedChars = new char[] { ' ', '\t', '\r', '\n' };
        private readonly char[] _incorrectChars = new char[] { '\r', '\n' };
        private readonly char[] _keywordOrNumberChars = "-0123456789.truefalsnl".ToCharArray();
        private readonly string[] _keywords = { "true", "false", "null" };

        private bool _errorFound;
        private bool _searchMode;
        private string _searchPath;

        public bool TrimComplexValues { get => this._trimComplexValues; set => this._trimComplexValues = value; }
        public bool SaveComplexValues { get => this._saveAllValues; set => this._saveAllValues = value; }
        public char JsonPathDivider { get => this._jsonPathDivider; set => this._jsonPathDivider = value; }
        public string RootName { get => this._rootName; set => this._rootName = value; }
        public bool SearchStartOnly { get => this._fastSearch; set => this._fastSearch = value; }

        public IEnumerable<ParsedProperty> ParseJsonToPathList(string json, out int endPosition, out bool errorFound)
        {
            _searchMode = false;
            _searchPath = "";
            var result = StartParser(json, out endPosition, out errorFound);

            return result;
        }

        private IEnumerable<ParsedProperty> StartParser(string json, out int endPosition, out bool errorFound)
        {
            _jsonText = json;
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

        public IEnumerable<ParsedProperty> ParseJsonToPathList(string json)
        {
            return StartParser(json, out var _, out var _);
        }

        public ParsedProperty SearchJsonPath(string json, string path)
        {
            _searchMode = true;
            _searchPath = path;
            var items = StartParser(json, out var _, out var _).ToArray();

            if (!items.Any())
                return null;

            return items.Where(n => n.Path == path).FirstOrDefault();
        }

        public bool GetLinesNumber(string json, int startPosition, int endPosition, out int startLine, out int endLine)
        {
            startLine = CountLinesFast(json, 0, startPosition);
            endLine = startLine + CountLinesFast(json, startPosition, endPosition);

            return true;
        }

        private int FindStartOfNextToken(int pos, out PropertyType foundObjectType)
        {
            foundObjectType = new PropertyType();
            var allowedChars = new[] { ' ', '\t', '\r', '\n', ',' };

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
                        if (_keywordOrNumberChars.Contains(currentChar))
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

        private int GetComment(int pos, string currentPath)
        {
            if (_searchMode)
            {
                var lastItem = _pathIndex?.LastOrDefault();
                if (lastItem?.Path == _searchPath)
                {
                    if (_fastSearch
                        || (!_fastSearch
                        && lastItem?.PropertyType != PropertyType.Array
                        && lastItem?.PropertyType != PropertyType.Object))
                    {
                        _errorFound = true;
                        return pos;
                    }
                }
                else
                {
                    _pathIndex?.Remove(_pathIndex?.LastOrDefault());
                }
            }

            var newElement = new ParsedProperty
            {
                PropertyType = PropertyType.Comment,
                StartPosition = pos,
                Path = currentPath,
                ValueType = JsonValueType.NotProperty
            };
            _pathIndex?.Add(newElement);

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

                    pos--;
                    newElement.EndPosition = pos;
                    newElement.Value = _jsonText.Substring(newElement.StartPosition + 2,
                        newElement.EndPosition - newElement.StartPosition + 1);

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

        private int GetPropertyName(int pos, string currentPath)
        {
            if (_searchMode)
            {
                var lastItem = _pathIndex?.LastOrDefault();
                if (lastItem?.Path == _searchPath)
                {
                    if (_fastSearch
                        || (!_fastSearch
                        && lastItem?.PropertyType != PropertyType.Array
                        && lastItem?.PropertyType != PropertyType.Object))
                    {
                        _errorFound = true;
                        return pos;
                    }
                }
                else
                {
                    _pathIndex?.Remove(_pathIndex?.LastOrDefault());
                }
            }

            var newElement = new ParsedProperty
            {
                StartPosition = pos
            };
            _pathIndex?.Add(newElement);

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

                    if (_escapeChars.Contains(_jsonText[pos])) // if \u0000
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

                    currentPath += _jsonPathDivider + newElement.Name;
                    newElement.Path = currentPath;
                    switch (_jsonText[pos])
                    {
                        //it's an object
                        case '{':
                            newElement.PropertyType = PropertyType.Object;
                            newElement.EndPosition = pos = GetObject(pos, currentPath, false);
                            newElement.ValueType = JsonValueType.NotProperty;

                            if (_saveAllValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                newElement.EndPosition - newElement.StartPosition + 1);

                                if (_trimComplexValues)
                                {
                                    newElement.Value = TrimObjectValue(newElement.Value);
                                }
                            }

                            return pos;
                        //it's an array
                        case '[':
                            newElement.PropertyType = PropertyType.Array;
                            newElement.EndPosition = pos = GetArray(pos, currentPath);
                            newElement.ValueType = JsonValueType.NotProperty;

                            if (_saveAllValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1);

                                if (_trimComplexValues)
                                {
                                    newElement.Value = TrimArrayValue(newElement.Value);
                                }
                            }

                            return pos;
                        // it's a property
                        default:
                            newElement.PropertyType = PropertyType.Property;
                            newElement.EndPosition = pos;
                            var newValue = _jsonText.Substring(valueStartPosition, pos - valueStartPosition + 1)
                                   .Trim();
                            newElement.ValueType = GetVariableType(newValue);
                            newElement.Value = newElement.ValueType == JsonValueType.String ? newValue.Trim('\"') : newValue;
                            return pos;
                    }
                }
                else if (_incorrectChars.Contains(currentChar)) // check restricted chars
                {
                    _errorFound = true;
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private int GetKeywordOrNumber(int pos, string currentPath, bool isArray)
        {
            if (_searchMode)
            {
                var lastItem = _pathIndex?.LastOrDefault();
                if (lastItem?.Path == _searchPath)
                {
                    if (_fastSearch
                        || (!_fastSearch
                        && lastItem?.PropertyType != PropertyType.Array
                        && lastItem?.PropertyType != PropertyType.Object))
                    {
                        _errorFound = true;
                        return pos;
                    }
                }
                else
                {
                    _pathIndex?.Remove(_pathIndex?.LastOrDefault());
                }
            }

            var newElement = new ParsedProperty
            {
                StartPosition = pos
            };
            _pathIndex?.Add(newElement);

            var endingChars = new[] { ',', '}', ']', '\r', '\n', '/' };

            for (; pos < _jsonText.Length; pos++) // searching for token end
            {
                var currentChar = _jsonText[pos];
                // end of token found
                if (endingChars.Contains(currentChar))
                {
                    pos--;
                    var newValue = _jsonText.Substring(newElement.StartPosition, pos - newElement.StartPosition + 1)
                           .Trim();

                    if (!_keywords.Contains(newValue)
                        && !IsNumeric(newValue))
                    {
                        _errorFound = true;
                        return pos;
                    }

                    newElement.Value = newValue;
                    newElement.PropertyType = isArray ? PropertyType.ArrayValue : PropertyType.Property;
                    newElement.EndPosition = pos;
                    newElement.Path = currentPath;
                    newElement.ValueType = GetVariableType(newValue);

                    return pos;
                }

                if (!_keywordOrNumberChars.Contains(currentChar)) // check restricted chars
                {
                    _errorFound = true;
                    return pos;
                }
            }

            _errorFound = true;
            return pos;
        }

        private int GetPropertyDivider(int pos, string currentPath)
        {
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
                        if (!_allowedChars.Contains(_jsonText[pos]))
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

        private int GetPropertyValue(int pos, string currentPath)
        {
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

                                if (_escapeChars.Contains(_jsonText[pos])) // if \u0000
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
                            else if (_incorrectChars.Contains(_jsonText[pos])) // check restricted chars
                            {
                                _errorFound = true;
                                return pos;
                            }
                        }

                        _errorFound = true;
                        return pos;
                    }
                    default:
                        if (!_allowedChars.Contains(_jsonText[pos])) // it's a property non-string value
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

                                if (!_keywordOrNumberChars.Contains(_jsonText[pos])) // check restricted chars
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

        private int GetArray(int pos, string currentPath)
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
                        if (_searchMode && currentPath == _searchPath)
                        {
                            _errorFound = true;
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

        private int GetObject(int pos, string currentPath, bool save = true)
        {
            if (_searchMode)
            {
                var lastItem = _pathIndex?.LastOrDefault();
                if (lastItem?.Path == _searchPath)
                {
                    if (_fastSearch
                        || (!_fastSearch
                        && lastItem?.PropertyType != PropertyType.Array
                        && lastItem?.PropertyType != PropertyType.Object))
                    {
                        _errorFound = true;
                        return pos;
                    }
                }
                else
                {
                    _pathIndex?.Remove(_pathIndex?.LastOrDefault());
                }
            }

            var newElement = new ParsedProperty();
            if (save)
            {
                newElement.StartPosition = pos;
                newElement.PropertyType = PropertyType.Object;
                newElement.Path = currentPath;
                newElement.ValueType = JsonValueType.NotProperty;
                _pathIndex?.Add(newElement);
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
                        if (save)
                        {
                            newElement.EndPosition = pos;
                            if (_saveAllValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1);

                                if (_trimComplexValues)
                                {
                                    newElement.Value = TrimObjectValue(newElement.Value);
                                }
                            }

                            if (_searchMode)
                            {
                                if (currentPath == _searchPath)
                                {
                                    _errorFound = true;
                                    return pos;
                                }
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

        private bool IsNumeric(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }

            return str.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
        }

        public JsonValueType GetVariableType(string text)
        {
            var type = JsonValueType.Unknown;

            if (string.IsNullOrEmpty(text))
            {
                type = JsonValueType.Unknown;
            }
            else if (IsNumeric(text))
            {
                type = JsonValueType.Number;
            }
            else if (text == "null")
            {
                type = JsonValueType.Null;
            }
            else if (text == "true" || text == "false")
            {
                type = JsonValueType.Boolean;
            }
            else if (text.Length > 1 && text[0] == ('\"') && text[text.Length - 1] == ('\"'))
            {
                type = JsonValueType.String;
            }
            return type;
        }

        public string TrimObjectValue(string objectText)
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

        public string TrimArrayValue(string arrayText)
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

        // fool-proof
        public static int CountLines(string text, int startIndex, int endIndex)
        {
            if (startIndex >= text.Length)
                return -1;

            if (startIndex > endIndex)
            {
                var n = startIndex;
                startIndex = endIndex;
                endIndex = n;
            }

            if (endIndex >= text.Length)
                endIndex = text.Length;

            var linesCount = 0;
            for (; startIndex < endIndex; startIndex++)
            {
                if (text[startIndex] != '\r' && text[startIndex] != '\n')
                    continue;

                linesCount++;
                if (startIndex < endIndex - 1
                    && text[startIndex] != text[startIndex + 1]
                    && (text[startIndex + 1] == '\r' || text[startIndex + 1] == '\n'))
                    startIndex++;
            }

            return linesCount;
        }

        static int CountLinesFast(string s, int startIndex, int endIndex)
        {
            int count = 0;
            while ((startIndex = s.IndexOf('\n', startIndex)) != -1
                && startIndex < endIndex)
            {
                count++;
                startIndex++;
            }
            return count;
        }
    }
}
