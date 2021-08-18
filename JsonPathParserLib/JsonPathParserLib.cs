using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace JsonPathParserLib
{
    public enum JsonPropertyTypes
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

    public enum JsonValueTypes
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
        public JsonPropertyTypes JsonPropertyType = JsonPropertyTypes.Unknown;
        public JsonValueTypes ValueType;

        public int RawLength
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

        private List<ParsedProperty> _pathIndex;

        private readonly char[] _escapeChars = new char[] { '\"', '\\', '/', 'b', 'f', 'n', 'r', 't', 'u' };
        private readonly char[] _allowedChars = new char[] { ' ', '\t', '\r', '\n' };
        private readonly char[] _incorrectChars = new char[] { '\r', '\n' };
        private readonly char[] _keywordOrNumberChars = "-0123456789.truefalsnl".ToCharArray();
        private readonly string[] _keywords = { "true", "false", "null" };

        private bool _errorFound;
        private bool _searchMode;
        private string _searchPath;

        public bool TrimComplexValues { get; set; }

        public bool SaveComplexValues { get; set; }

        public char JsonPathDivider { get; set; } = '.';
        public string RootName { get; set; } = "root";
        public bool SearchStartOnly { get; set; }

        public IEnumerable<ParsedProperty> ParseJsonToPathList(string jsonText, out int endPosition, out bool errorFound)
        {
            _searchMode = false;
            _searchPath = "";
            var result = StartParser(jsonText, out endPosition, out errorFound);

            return result;
        }

        private IEnumerable<ParsedProperty> StartParser(string jsonText, out int endPosition, out bool errorFound)
        {
            _jsonText = jsonText;
            endPosition = 0;
            _errorFound = false;
            _pathIndex = new List<ParsedProperty>();

            if (string.IsNullOrEmpty(jsonText))
            {
                errorFound = _errorFound;
                return _pathIndex;
            }

            var currentPath = RootName;
            while (!_errorFound && endPosition < _jsonText.Length)
            {
                endPosition = FindStartOfNextToken(endPosition, out var foundObjectType);
                if (_errorFound || endPosition >= _jsonText.Length)
                    break;

                switch (foundObjectType)
                {
                    case JsonPropertyTypes.Property:
                        endPosition = GetPropertyName(endPosition, currentPath);
                        break;
                    case JsonPropertyTypes.Comment:
                        endPosition = GetComment(endPosition, currentPath);
                        break;
                    case JsonPropertyTypes.Object:
                        endPosition = GetObject(endPosition, currentPath);
                        break;
                    case JsonPropertyTypes.EndOfObject:
                        break;
                    case JsonPropertyTypes.Array:
                        endPosition = GetArray(endPosition, currentPath);
                        break;
                    case JsonPropertyTypes.EndOfArray:
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

        public IEnumerable<ParsedProperty> ParseJsonToPathList(string jsonText)
        {
            return StartParser(jsonText, out var _, out var _);
        }

        public ParsedProperty SearchJsonPath(string jsonText, string path)
        {
            _searchMode = true;
            _searchPath = path;
            var items = StartParser(jsonText, out var _, out var _).ToArray();

            if (!items.Any())
                return null;

            return items.Where(n => n.Path == path).FirstOrDefault();
        }

        public bool GetLinesNumber(string jsonText, int startPosition, int endPosition, out int startLine, out int endLine)
        {
            startLine = CountLinesFast(jsonText, 0, startPosition);
            endLine = startLine + CountLinesFast(jsonText, startPosition, endPosition);

            return true;
        }

        private int FindStartOfNextToken(int pos, out JsonPropertyTypes foundObjectTypes)
        {
            foundObjectTypes = new JsonPropertyTypes();
            var allowedChars = new[] { ' ', '\t', '\r', '\n', ',' };

            for (; pos < _jsonText.Length; pos++)
            {
                var currentChar = _jsonText[pos];
                switch (currentChar)
                {
                    case '/':
                        foundObjectTypes = JsonPropertyTypes.Comment;
                        return pos;
                    case '\"':
                        foundObjectTypes = JsonPropertyTypes.Property;
                        return pos;
                    case '{':
                        foundObjectTypes = JsonPropertyTypes.Object;
                        return pos;
                    case '}':
                        foundObjectTypes = JsonPropertyTypes.EndOfObject;
                        return pos;
                    case '[':
                        foundObjectTypes = JsonPropertyTypes.Array;
                        return pos;
                    case ']':
                        foundObjectTypes = JsonPropertyTypes.EndOfArray;
                        return pos;
                    default:
                        {
                            if (_keywordOrNumberChars.Contains(currentChar))
                            {
                                foundObjectTypes = JsonPropertyTypes.KeywordOrNumberProperty;
                                return pos;
                            }

                            if (!allowedChars.Contains(currentChar))
                            {
                                foundObjectTypes = JsonPropertyTypes.Error;
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
                    if (SearchStartOnly
                        || (!SearchStartOnly
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Array
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Object))
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
                JsonPropertyType = JsonPropertyTypes.Comment,
                StartPosition = pos,
                Path = currentPath,
                ValueType = JsonValueTypes.NotProperty
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
                    if (SearchStartOnly
                        || (!SearchStartOnly
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Array
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Object))
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
                        newElement.JsonPropertyType = JsonPropertyTypes.ArrayValue;
                        newElement.EndPosition = pos;
                        newElement.Path = currentPath;
                        newElement.ValueType = GetVariableType(newName);
                        newElement.Value = newElement.ValueType == JsonValueTypes.String ? newName.Trim('\"') : newName;
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

                    currentPath += JsonPathDivider + newElement.Name;
                    newElement.Path = currentPath;
                    switch (_jsonText[pos])
                    {
                        //it's an object
                        case '{':
                            newElement.JsonPropertyType = JsonPropertyTypes.Object;
                            newElement.EndPosition = pos = GetObject(pos, currentPath, false);
                            newElement.ValueType = JsonValueTypes.NotProperty;

                            if (SaveComplexValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                newElement.EndPosition - newElement.StartPosition + 1);

                                if (TrimComplexValues)
                                {
                                    newElement.Value = TrimObjectValue(newElement.Value);
                                }
                            }

                            return pos;
                        //it's an array
                        case '[':
                            newElement.JsonPropertyType = JsonPropertyTypes.Array;
                            newElement.EndPosition = pos = GetArray(pos, currentPath);
                            newElement.ValueType = JsonValueTypes.NotProperty;

                            if (SaveComplexValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1);

                                if (TrimComplexValues)
                                {
                                    newElement.Value = TrimArrayValue(newElement.Value);
                                }
                            }

                            return pos;
                        // it's a property
                        default:
                            newElement.JsonPropertyType = JsonPropertyTypes.Property;
                            newElement.EndPosition = pos;
                            var newValue = _jsonText.Substring(valueStartPosition, pos - valueStartPosition + 1)
                                   .Trim();
                            newElement.ValueType = GetVariableType(newValue);
                            newElement.Value = newElement.ValueType == JsonValueTypes.String ? newValue.Trim('\"') : newValue;
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
                    if (SearchStartOnly
                        || (!SearchStartOnly
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Array
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Object))
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
                    newElement.JsonPropertyType = isArray ? JsonPropertyTypes.ArrayValue : JsonPropertyTypes.Property;
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
                    case JsonPropertyTypes.Comment:
                        pos = GetComment(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case JsonPropertyTypes.Property:
                        pos = GetPropertyName(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case JsonPropertyTypes.Object:
                        pos = GetObject(pos, currentPath + "[" + arrayIndex + "]");
                        arrayIndex++;
                        break;
                    case JsonPropertyTypes.KeywordOrNumberProperty:
                        pos = GetKeywordOrNumber(pos, currentPath + "[" + arrayIndex + "]", true);
                        arrayIndex++;
                        break;
                    case JsonPropertyTypes.EndOfArray:
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
                    if (SearchStartOnly
                        || (!SearchStartOnly
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Array
                        && lastItem?.JsonPropertyType != JsonPropertyTypes.Object))
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
                newElement.JsonPropertyType = JsonPropertyTypes.Object;
                newElement.Path = currentPath;
                newElement.ValueType = JsonValueTypes.NotProperty;
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
                    case JsonPropertyTypes.Comment:
                        pos = GetComment(pos, currentPath);
                        break;
                    case JsonPropertyTypes.Property:
                        pos = GetPropertyName(pos, currentPath);
                        break;
                    case JsonPropertyTypes.Object:
                        pos = GetObject(pos, currentPath);
                        break;
                    case JsonPropertyTypes.EndOfObject:
                        if (save)
                        {
                            newElement.EndPosition = pos;
                            if (SaveComplexValues)
                            {
                                newElement.Value = _jsonText.Substring(newElement.StartPosition,
                                    newElement.EndPosition - newElement.StartPosition + 1);

                                if (TrimComplexValues)
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

        public JsonValueTypes GetVariableType(string str)
        {
            var type = JsonValueTypes.Unknown;

            if (string.IsNullOrEmpty(str))
            {
                type = JsonValueTypes.Unknown;
            }
            else if (str.Length > 1 && str[0] == ('\"') && str[str.Length - 1] == ('\"'))
            {
                type = JsonValueTypes.String;
            }
            else if (str == "null")
            {
                type = JsonValueTypes.Null;
            }
            else if (str == "true" || str == "false")
            {
                type = JsonValueTypes.Boolean;
            }
            else if (IsNumeric(str))
            {
                type = JsonValueTypes.Number;
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
        public static int CountLines(string jsonText, int startIndex, int endIndex)
        {
            if (startIndex >= jsonText.Length)
                return -1;

            if (startIndex > endIndex)
            {
                var n = startIndex;
                startIndex = endIndex;
                endIndex = n;
            }

            if (endIndex >= jsonText.Length)
                endIndex = jsonText.Length;

            var linesCount = 0;
            for (; startIndex < endIndex; startIndex++)
            {
                if (jsonText[startIndex] != '\r' && jsonText[startIndex] != '\n')
                    continue;

                linesCount++;
                if (startIndex < endIndex - 1
                    && jsonText[startIndex] != jsonText[startIndex + 1]
                    && (jsonText[startIndex + 1] == '\r' || jsonText[startIndex + 1] == '\n'))
                    startIndex++;
            }

            return linesCount;
        }

        private static int CountLinesFast(string jsonText, int startIndex, int endIndex)
        {
            var count = 0;
            while ((startIndex = jsonText.IndexOf('\n', startIndex)) != -1
                && startIndex < endIndex)
            {
                count++;
                startIndex++;
            }
            return count;
        }

        public IEnumerable<ParsedProperty> ConvertForTreeProcessing(IEnumerable<ParsedProperty> schemaProperties)
        {
            var result = new List<ParsedProperty>();
            var tmpStr = new StringBuilder();

            foreach (var property in schemaProperties)
            {
                var path = property.Path;
                tmpStr.Append(path);
                var pos = path.IndexOf('[');
                while (pos >= 0)
                {
                    tmpStr.Insert(pos, JsonPathDivider);
                    pos = path.IndexOf('[', pos + 1);
                }

                path = tmpStr.ToString();

                var name = property.Name;
                if (string.IsNullOrEmpty(name) && path[path.Length - 1] == ']')
                {
                    pos = path.LastIndexOf('[');
                    if (pos >= 0)
                        name = path.Substring(pos);
                }

                var newProperty = new ParsedProperty
                {
                    Name = name,
                    Path = path,
                    JsonPropertyType = property.JsonPropertyType,
                    EndPosition = property.EndPosition,
                    StartPosition = property.StartPosition,
                    Value = property.Value,
                    ValueType = property.ValueType,
                };
                result.Add(newProperty);
                tmpStr.Clear();
            }

            return result;
        }
    }
}
