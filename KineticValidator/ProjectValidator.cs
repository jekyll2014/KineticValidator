// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using JsonPathParserLib;

using NJsonSchema;
using NJsonSchema.Validation;

namespace KineticValidator
{
    public static class ProjectValidator
    {
        //constants from caller
        private static char _splitChar;
        private static string _schemaTag;
        private static string _backupSchemaExtension;
        private static string _fileMask;
        private static string _serverAssembliesPath;
        private static IEnumerable<ContentTypeItem> _fileTypes;

        //setting flags from UI
        private static bool _ignoreHttpsError;
        private static bool _skipSchemaErrors;
        private static bool _patchAllFields;

        //settings from .config
        private static string[] _systemMacros;
        private static string[] _systemDataViews;
        private static IEnumerable<ValidationErrorKind> _suppressSchemaErrors;

        //project folder related settings
        private static string _projectName;
        private static string _projectPath;
        private static FolderType _folderType;

        //only set
        private static ConcurrentDictionary<string, string> _processedFilesList; // file name / schema URL
        private static IEnumerable<JsonProperty> _jsonPropertiesCollection;
        private static IEnumerable<ReportItem> _runValidationReportsCollection;
        private static IEnumerable<ReportItem> _deserializeFileReportsCollection;
        private static IEnumerable<ReportItem> _parseJsonObjectReportsCollection;

        internal static Dictionary<string, Func<string, IEnumerable<ReportItem>>> ValidatorsList =>
            new Dictionary<string, Func<string, IEnumerable<ReportItem>>>
            {
                {"File list validation", RunValidation},
                {"Deserialization validation", DeserializeFile},
                {"JSON properties processing", ParseJsonObject},
                {"Schema validation", SchemaValidation},
                {"Redundant files", RedundantFiles},
                {"National characters", ValidateFileChars},
                {"Duplicate JSON property names", DuplicateIds},
                {"Empty id's", EmptyIdNames},
                {"Redundant patches", RedundantPatches},
                {"Non existing patches", CallNonExistingPatches},
                {"Overriding patches", OverridingPatches},
                {"Possible patches", PossiblePatchValues},
                {"Hard-coded message strings", HardCodedStrings},
                {"Possible strings", PossibleStringsValues},
                {"Empty string names", EmptyStringNames},
                {"Empty string values", EmptyStringValues},
                {"Redundant strings", RedundantStrings},
                {"Non existing strings", CallNonExistingStrings},
                {"Overriding strings", OverridingStrings},
                {"Empty events", EmptyEvents},
                {"Overriding events", OverridingEvents},
                {"Redundant events", RedundantEvents},
                {"Non existing events", CallNonExistingEvents},
                {"Redundant dataViews", RedundantDataViews},
                {"Non existing dataViews", CallNonExistingDataViews},
                {"Overriding dataViews", OverridingDataViews},
                {"Non existing dataTables", CallNonExistingDataTables},
                {"Empty rules", EmptyRules},
                {"No condition rules", NoConditionRules},
                {"Overriding rules", OverridingRules},
                {"Overriding tools", OverridingTools},
                {"Missing forms", MissingForms},
                {"Missing searches", MissingSearches},
                {"JavaScript code", JsCode},
                {"JS #_trans.dataView('DataView').count_#", JsDataViewCount},
                {"Incorrect dataview-condition", IncorrectDvConditionViewName},
                {"Incorrect REST calls", IncorrectRestCalls},
                {"Incorrect field names", IncorrectFieldUsages},
                {"Un-enclosed data-field value", UnEnclosedFieldValue},
                {"Missing layout id's", MissingLayoutIds},
                {"Incorrect event expressions", IncorrectEventExpression},
                {"Incorrect rule conditions", IncorrectRuleConditions},
                {"'providerModel' mixed with 'clientFilter'", ProviderModelMixedWithClientFilter },
                {"Property name contains \'.\' char", DottedPropertyNames},
                // questionable validations
                {"Incorrect layout id's", IncorrectLayoutIds},
                {"Incorrect tab links", IncorrectTabIds},
                {"Duplicate GUID's", DuplicateGuiDs}
            };

        //cached data
        private static Dictionary<string, string> _patchValues; // name / value

        private static ConcurrentDictionary<string, List<ParsedProperty>> _parsedFiles =
            new ConcurrentDictionary<string, List<ParsedProperty>>();

        //global cache
        private static ConcurrentDictionary<string, string> SchemaList =
            new ConcurrentDictionary<string, string>(); // schema URL / schema text

        private static Dictionary<string, Dictionary<string, string[]>> KnownServices =
            new Dictionary<string, Dictionary<string, string[]>>(); // svcName / [methodName, parameters]

        private class KineticDataView
        {
            public string SvcName = "";
            public string ServerDataSetName = "";
            public string LocalDataSetName = "";

            public string DataViewName = "";
            public string LocalDataTableName = "";
            public string ServerDataTableName = "";

            public bool IsLocal = false;
            public bool IsDataViewCondition = false;

            public List<string> Fields = new List<string>();
            public List<string> AdditionalFields = new List<string>();
            public List<string> AllFields
            {
                get
                {
                    var allFields = new List<string>();
                    allFields.AddRange(Fields);
                    allFields.AddRange(AdditionalFields);
                    return allFields;
                }
            }
        }

        private static List<KineticDataView>
            _formDataViews = new List<KineticDataView>();

        private static Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>
            KnownDataSets =
                new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>(); // svcName / [dataSet, [datatables, fields]]

        internal static void Initialize(ProcessConfiguration processConfiguration,
            ProjectConfiguration projectConfiguration,
            SeedData seedData,
            bool clearCache)
        {
            _splitChar = processConfiguration.SplitChar;
            _schemaTag = processConfiguration.SchemaTag;
            _backupSchemaExtension = processConfiguration.BackupSchemaExtension;
            _fileMask = processConfiguration.FileMask;
            _fileTypes = processConfiguration.FileTypes;
            _ignoreHttpsError = processConfiguration.IgnoreHttpsError;
            _skipSchemaErrors = processConfiguration.SkipSchemaErrors;
            _patchAllFields = processConfiguration.PatchAllFields;
            _systemMacros = processConfiguration.SystemMacros;
            _systemDataViews = processConfiguration.SystemDataViews;
            _suppressSchemaErrors = processConfiguration.SuppressSchemaErrors;
            _serverAssembliesPath = processConfiguration.ServerAssembliesPath;

            if (projectConfiguration != null)
            {
                _projectName = projectConfiguration.ProjectName;
                _projectPath = projectConfiguration.ProjectPath;
                _folderType = projectConfiguration.FolderType;
            }

            if (seedData != null)
            {
                //only set
                _processedFilesList = seedData.ProcessedFilesList;
                _jsonPropertiesCollection = seedData.JsonPropertiesCollection;
                _runValidationReportsCollection = seedData.RunValidationReportsCollection;
                _deserializeFileReportsCollection = seedData.DeserializeFileReportsCollection;
                _parseJsonObjectReportsCollection = seedData.ParseJsonObjectReportsCollection;
            }

            _patchValues = new Dictionary<string, string>();

            if (clearCache)
            {
                _parsedFiles = new ConcurrentDictionary<string, List<ParsedProperty>>();
            }
            //SchemaList = new Dictionary<string, string>();
            //KnownServices = new Dictionary<string, Dictionary<string, string[]>>();
            //KnownDataSets = new Dictionary<string, Dictionary<string, Dictionary<string, string[]>>>();
            _formDataViews = new List<KineticDataView>();

            if (_patchAllFields)
                PatchAllFields();
        }

        #region Helping methods

        private struct Brackets
        {
            public int Pos;
            public int Level;
            public char Bracket;
            public int Number;
        }

        private static List<string> GetTableField(string field, bool noBracketTrim = false)
        {
            var valueList = new List<string>();
            if (string.IsNullOrEmpty(field))
                return valueList;

            const char startChar = '{';
            const char endChar = '}';
            var improperChars = new[] { ' ', ',', ':', ';', '\'', '\"', '(', ')', '+', '=', '[', ']', '&', '|', '~' };

            var tokens = field.Split(improperChars);
            foreach (var token in tokens)
            {
                if (string.IsNullOrEmpty(token) || !token.Contains('.'))
                    continue;

                var startCharNum = CountChars(token, startChar);
                var endCharNum = CountChars(token, endChar);
                if (startCharNum != 0 && endCharNum != 0 && startCharNum == endCharNum)
                {
                    var l = 0;
                    var n = 0;
                    var sequence = new List<Brackets>();
                    for (var i = 0; i < token.Length; i++)
                    {
                        if (token[i] == startChar)
                        {
                            l++;
                            sequence.Add(new Brackets
                            {
                                Pos = i,
                                Level = l,
                                Bracket = startChar,
                                Number = n
                            });
                            n++;
                        }
                        else if (token[i] == endChar)
                        {
                            sequence.Add(new Brackets
                            {
                                Pos = i,
                                Level = l,
                                Bracket = endChar,
                                Number = n
                            });
                            n++;
                            l--;
                        }
                    }

                    l = sequence.Max(brackets => brackets.Level);
                    for (; l > 0; l--)
                    {
                        var s = sequence.Where(m => m.Level == l).ToArray();
                        for (var i = 1; i < s.Length; i++)
                        {
                            if (s[i - 1].Number + 1 == s[i].Number && s[i - 1].Bracket == startChar &&
                                s[i].Bracket == endChar)
                            {
                                var startPos = s[i - 1].Pos + 1;
                                var endPos = s[i].Pos - s[i - 1].Pos - 1;
                                if (noBracketTrim)
                                {
                                    if (startPos > 0 && token[startPos - 1] == '{')
                                        startPos--;
                                    if (endPos + 2 < token.Length && token[endPos + 2] == '}')
                                        endPos += 2;
                                }

                                var str = token.Substring(startPos, endPos);

                                if (str.Contains('.'))
                                    valueList.Add(str);
                            }
                        }
                    }
                }
                else if (noBracketTrim)
                {
                    valueList.Add(token);
                }
            }

            return valueList;
        }

        private static int CountChars(string data, char countChar)
        {
            return data.Count(ch => ch == countChar);
        }

        private static IEnumerable<string> GetPatchList(string field)
        {
            var patchList = new List<string>();
            if (string.IsNullOrEmpty(field))
                return patchList;

            var improperChars = new[] { ' ', '.', ',', ':', ';', '\'', '\"', '(', ')', '{', '}', '[', ']', '~' };
            var tokens = field.Split(improperChars);
            const char tokenEmbracementChar = '%';
            foreach (var token in tokens)
            {
                var c = CountChars(field, tokenEmbracementChar);
                if (c != 0 && c % 2 == 0)
                {
                    var pos1 = 0;
                    do
                    {
                        pos1 = token.IndexOf(tokenEmbracementChar, pos1);
                        if (pos1 >= 0 && pos1 < token.Length - 2)
                        {
                            var pos2 = token.LastIndexOf(tokenEmbracementChar);

                            if (pos2 >= 0 && pos2 - pos1 > 1)
                            {
                                var newPatch = token.Substring(pos1, pos2 - pos1 + 1);
                                if (newPatch.IndexOf(tokenEmbracementChar, 1, newPatch.Length - 2) >= 0)
                                {
                                    // stack overflow comes on "%%patch%" processing. Easy way to handle.
                                    if (newPatch.StartsWith("%%") && newPatch.EndsWith("%"))
                                        pos1 = pos2 + 1;
                                    else
                                        patchList.AddRange(GetPatchList(newPatch));
                                }
                                else
                                {
                                    patchList.Add(newPatch);
                                    pos1 = pos2 + 1;
                                }
                            }
                            else
                            {
                                pos1++;
                            }
                        }
                    } while (pos1 >= 0 && pos1 < token.Length - 2);
                }
            }

            return patchList;
        }

        private static string GetParentName(string parentPath)
        {
            var i = parentPath.LastIndexOf('.');

            return parentPath.Substring(i + 1);
        }

        private static string TrimPathEnd(string originalPath, int levels)
        {
            for (; levels > 0; levels--)
            {
                var pos = originalPath.LastIndexOf('.');
                if (pos >= 0)
                {
                    originalPath = originalPath.Substring(0, pos);
                }
                else
                    break;
            }

            return originalPath;
        }

        private static string TrimArrayName(string arrayName)
        {
            if (arrayName.EndsWith("]"))
            {
                var pos = arrayName.LastIndexOf('[');
                if (pos >= 0)
                {
                    arrayName = arrayName.Substring(0, pos);
                }
            }

            return arrayName;
        }

        private static bool DataViewExists(string dataField)
        {
            var tokens = dataField.Trim(new[] { '{', '}' }).Split('.');

            return _formDataViews.Any(n =>
                n.DataViewName == tokens[0]);
        }

        private static bool DataFieldExists(string dataField)
        {
            var tokens = dataField.Trim(new[] { '{', '}', '[', ']' }).Split('.');
            for (var i = 0; i < tokens.Length; i++)
            {
                tokens[i] = tokens[i].Trim(new[] { '[', ']' });
            }

            if (tokens.Length != 2)
                return false;

            var dv = _formDataViews.FirstOrDefault(n =>
                n.DataViewName == tokens[0]);

            if (dv == null)
                return false;

            if (_systemDataViews.Contains(tokens[0], StringComparer.InvariantCultureIgnoreCase))
                return true;

            var result = dv.Fields.Contains(tokens[1]);
            if (!result)
            {
                result = dv.AdditionalFields.Contains(tokens[1]);
            }

            return result;
        }

        private static bool IsTableField(string field)
        {
            var regex = new Regex(@"^{+\w+[.]+\w+}$");

            return regex.Match(field.Trim('\'')).Success;
        }

        private static bool IsPatch(string field)
        {
            var regex = new Regex(@"^%+\w+%$");

            return regex.Match(field.Trim('\'')).Success;
        }

        private static bool IsNumeric(string str)
        {
            if (string.IsNullOrEmpty(str))
            {
                return false;
            }

            return str.All(c => (c >= '0' && c <= '9') || c == '.' || c == '-');
        }

        private static bool HasJsCode(string field)
        {
            return field.Contains("#_") && field.Contains("_#");
        }

        private static bool HasJsDvCount(string field)
        {
            //"#_*trans.dataView(*).count*_#"
            //Regex regex = new Regex(@"^#_+[\s\S\w]+rans.dataView\(+[\s\w]+\).count+[\s\S\w]_#$");
            //return regex.Match(field).Success;
            return field.Contains("trans.dataView(") && field.Contains(").count");
        }

        private static readonly object LockSchemaGetter = new object();
        private static async Task<List<ReportItem>> ValidateFileSchema(string projectName,
            string fullFileName,
            string schemaUrl = "")
        {
            var report = new List<ReportItem>();
            string jsonText;
            try
            {
                jsonText = File.ReadAllText(fullFileName);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = "File read exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.File.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            if (string.IsNullOrEmpty(schemaUrl))
            {
                var versionIndex = jsonText.IndexOf(_schemaTag, StringComparison.Ordinal);
                if (versionIndex <= 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = projectName,
                        FullFileName = fullFileName,
                        Message = "Schema property not found",
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileSchema"
                    };
                    report.Add(reportItem);

                    return report;
                }

                versionIndex += _schemaTag.Length;
                while (versionIndex < jsonText.Length
                       && jsonText[versionIndex] != '"'
                       && jsonText[versionIndex] != '\r'
                       && jsonText[versionIndex] != '\n')
                    versionIndex++;

                versionIndex++;
                var strEnd = versionIndex;
                while (strEnd < jsonText.Length
                       && jsonText[strEnd] != '"'
                       && jsonText[strEnd] != '\r'
                       && jsonText[strEnd] != '\n')
                    strEnd++;

                if (versionIndex >= 0 && jsonText.Length > versionIndex)
                    schemaUrl = jsonText.Substring(versionIndex, strEnd - versionIndex).Trim();
            }

            if (string.IsNullOrEmpty(schemaUrl) || !schemaUrl.EndsWith(".json"))
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"URL incorrect [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            if (!SchemaList.ContainsKey(schemaUrl))
            {
                lock (LockSchemaGetter)
                {
                    if (!SchemaList.ContainsKey(schemaUrl))
                    {
                        var schemaData = "";
                        try
                        {
                            schemaData = GetSchemaText(schemaUrl);
                        }
                        catch (Exception ex)
                        {
                            var reportItem = new ReportItem
                            {
                                ProjectName = projectName,
                                FullFileName = fullFileName,
                                Message = $"Schema download exception [{schemaUrl}]: {Utilities.ExceptionPrint(ex)}",
                                ValidationType = ValidationTypeEnum.Scheme.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = "ValidateFileSchema"
                            };
                            report.Add(reportItem);
                        }

                        SchemaList.TryAdd(schemaUrl, schemaData);
                    }
                }
            }

            var schemaText = SchemaList[schemaUrl];

            if (string.IsNullOrEmpty(schemaText))
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema is empty [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            JsonSchema schema;
            try
            {
                schema = await JsonSchema.FromJsonAsync(schemaText).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema parse exception [{schemaUrl}]: {Utilities.ExceptionPrint(ex)}",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            if (schema == null)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = $"Schema is empty [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            ICollection<ValidationError> errors;
            try
            {
                errors = schema.Validate(jsonText);
            }
            catch (Exception ex)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = projectName,
                    FullFileName = fullFileName,
                    Message = "File validation exception: " + Utilities.ExceptionPrint(ex),
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            foreach (var error in errors)
            {
                //var errorText = SchemaErrorToString(fullFileName, error);
                var errorList = GetErrorList(projectName, error);
                if (_skipSchemaErrors && _suppressSchemaErrors.Contains(error.Kind))
                {
                    //Utilities.SaveDevLog(errorText);
                }
                else
                {
                    var newReportList = errorList.Select(schemaError => new ReportItem
                    {
                        ProjectName = projectName,
                        FullFileName = fullFileName,
                        FileType = Utilities.GetFileTypeFromFileName(fullFileName, _fileTypes).ToString(),
                        LineId = schemaError.LineId,
                        JsonPath = schemaError.JsonPath.TrimStart('#', '/'),
                        Message = schemaError.Message,
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileSchema"
                    }).ToArray();

                    if (newReportList.Any())
                        report.AddRange(newReportList);
                }
            }

            return report;
        }

        private static IEnumerable<ReportItem> GetErrorList(string projectName, ValidationError error)
        {
            var errorList = new List<ReportItem>();
            if (error is ChildSchemaValidationError subErrorCollection)
            {
                errorList.AddRange(subErrorCollection.Errors.SelectMany(subError => subError.Value,
                    (subError, subErrorItem) => new ReportItem
                    {
                        ProjectName = projectName,
                        LineId = subErrorItem.LineNumber.ToString(),
                        JsonPath = subErrorItem.Path.TrimStart('#', '/'),
                        Message = subErrorItem.Kind.ToString(),
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "GetErrorList"
                    }));
            }
            else
            {
                var report = new ReportItem
                {
                    ProjectName = projectName,
                    JsonPath = error.Path.TrimStart('#', '/'),
                    Message = error.Kind.ToString(),
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "GetErrorList"
                };
                errorList.Add(report);
            }

            return errorList;
        }

        private static string SchemaErrorToString(string fullFileName, ValidationError error)
        {
            var errorText = new StringBuilder();
            errorText.AppendLine(fullFileName
                                 + ": line #"
                                 + error.LineNumber
                                 + " "
                                 + error.Kind
                                 + ", path="
                                 + error.Path);

            if (!(error is ChildSchemaValidationError subErrorCollection))
                return errorText.ToString();

            foreach (var subError in subErrorCollection.Errors)
            {
                foreach (var subErrorItem in subError.Value)
                {
                    errorText.AppendLine("\t"
                                         + "- line #"
                                         + subErrorItem.LineNumber
                                         + " "
                                         + subErrorItem.Kind
                                         + ", path="
                                         + subErrorItem.Path);
                }
            }

            return errorText.ToString();
        }

        private static string GetSchemaText(string schemaUrl)
        {
            var schemaData = "";
            if (string.IsNullOrEmpty(schemaUrl))
                return schemaData;

            var localPath = GetLocalUrlPath(schemaUrl);
            if (File.Exists(localPath))
            {
                schemaData = File.ReadAllText(localPath);
            }
            else
            {
                if (_ignoreHttpsError)
                    ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;

                using (var webClient = new WebClient())
                {
                    schemaData = webClient.DownloadString(schemaUrl);
                    var dirPath = Path.GetDirectoryName(localPath);
                    if (dirPath != null)
                    {
                        try
                        {
                            if (!Directory.Exists(dirPath))
                                Directory.CreateDirectory(dirPath);

                            File.WriteAllText(localPath + _backupSchemaExtension, schemaData);
                        }
                        catch (Exception ex)
                        {
                            Utilities.SaveDevLog(ex.Message);
                            return schemaData;
                        }
                    }
                }
            }

            return schemaData;
        }

        private static string GetLocalUrlPath(string url)
        {
            if (!url.Contains("://"))
                return "";

            url = url.Replace("://", "");
            if (!url.Contains("/"))
                return "";

            var currentDirectory = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName);
            var i = url.IndexOf('/');
            if (i < 0 || url.Length <= url.IndexOf('/'))
                return "";

            var localPath = currentDirectory + url.Substring(i);
            localPath = localPath.Replace('/', '\\');
            return localPath;
        }

        private static IEnumerable<ParsedProperty> GetParsedFile(string file)
        {
            var list = new List<ParsedProperty>();
            if (!_parsedFiles.ContainsKey(file))
            {
                string json;
                try
                {
                    json = File.ReadAllText(file);
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                    return list;
                }

                var parser = new JsonPathParser
                {
                    TrimComplexValues = false,
                    SaveComplexValues = false,
                    RootName = "",
                    JsonPathDivider = '.'
                };

                list = parser.ParseJsonToPathList(json.Replace(' ', ' '), out _, out _).ToList();
                _parsedFiles.TryAdd(file, list);
            }
            else
            {
                list = _parsedFiles[file];
            }

            return list;
        }

        private static void PatchAllFields()
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

#if DEBUG
            var startTime = DateTime.Now;
#endif
            var valuesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.Value.Contains('%'))
                .ToArray();

            if (valuesList.Any())
            {
                foreach (var item in valuesList)
                {
                    foreach (var patch in _patchValues)
                    {
                        item.PatchedValue = item.PatchedValue.Replace(patch.Key, patch.Value);
                        if (!item.PatchedValue.Contains('%'))
                            break;
                    }
                }
            }
#if DEBUG
            Console.WriteLine($"PatchAllFields execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
        }

        private static Dictionary<string, string> CollectPatchValues()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id");

            var patchValues = new Dictionary<string, string>();
            foreach (var item in patchList)
            {
                var newValue = _jsonPropertiesCollection
                    .LastOrDefault(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Patch
                        && n.FullFileName == item.FullFileName
                        && n.ParentPath == item.ParentPath
                        && n.Name == "value")
                    ?.Value;

                var patchName = "%" + item.Value + "%";
                if (!patchValues.ContainsKey(patchName))
                    patchValues.Add(patchName, newValue);
                else
                    patchValues[patchName] = newValue;
            }

            var allReplaced = false;
            do
            {
                allReplaced = true;

                var toPatch = patchValues.Where(n =>
                    n.Value.Trim().StartsWith("%")
                    && n.Value.Trim().EndsWith("%")).ToArray();


                if (toPatch.Any())
                {
                    var replaceValues = new Dictionary<string, string>();
                    foreach (var item in toPatch)
                    {
                        foreach (var patch in patchValues.Where(patch => patch.Key == item.Value))
                        {
                            replaceValues.Add(item.Key, patch.Value);
                            allReplaced = false;
                            break;
                        }
                    }

                    foreach (var r in replaceValues)
                        patchValues[r.Key] = r.Value;
                }
            } while (!allReplaced);

            return patchValues;
        }

        private struct RestMethodInfo
        {
            public string SvcName;
            public string MethodName;
            public string[] Parameters;
        }

        private static readonly object LockServiceLoader = new object();
        private static bool LoadService(string svcName, string assemblyPath)
        {
            if (KnownServices.ContainsKey(svcName) && KnownDataSets.ContainsKey(svcName))
                return true;

            lock (LockServiceLoader)
            {
                if (KnownServices.ContainsKey(svcName) && KnownDataSets.ContainsKey(svcName))
                    return true;

                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence,
                new AppDomainSetup
                {
                    ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location)
                });
                try
                {
                    var loader =
                        (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName,
                            typeof(AssemblyLoader).FullName ?? string.Empty);

                    var assemblyLoadResult = loader.LoadAssembly(svcName, assemblyPath);

                    if (!assemblyLoadResult)
                        return false;

                    KnownServices.Add(svcName, loader.AllMethodsInfo);
                    KnownDataSets.Add(svcName, loader.AllDataSetsInfo);
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                    return false;
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }

            return true;
        }

        private static RestMethodInfo GetRestMethod(string svcName, string methodName, string assemblyPath)
        {
            var result = new RestMethodInfo();

            if (KnownServices.ContainsKey(svcName))
            {
                result.SvcName = svcName;
                KnownServices.TryGetValue(svcName, out var methods);
                if (methods != null && methods.ContainsKey(methodName))
                {
                    result.MethodName = methodName;
                    methods.TryGetValue(methodName, out result.Parameters);
                }
            }
            else
            {
                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence,
                    new AppDomainSetup
                    {
                        ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location)
                    });
                try
                {
                    var loader =
                        (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName,
                            typeof(AssemblyLoader).FullName ?? string.Empty);

                    var assemblyLoadResult = loader.LoadAssembly(svcName, assemblyPath);

                    if (!assemblyLoadResult)
                        return result;

                    result.SvcName = svcName;
                    KnownServices.Add(svcName, loader.AllMethodsInfo);
                    if (loader.AllMethodsInfo != null && loader.AllMethodsInfo.ContainsKey(methodName))
                    {
                        result.MethodName = methodName;
                        loader.AllMethodsInfo.TryGetValue(methodName, out result.Parameters);
                    }
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }

            return result;
        }

        private struct RestDataTableInfo
        {
            public string SvcName;
            public string DataSetName;
            public string TableName;
            public string[] Fields;
        }

        private static RestDataTableInfo GetRestServiceDataTable(string svcName, string dataSetName, string tableName,
            string assemblyPath)
        {
            var result = new RestDataTableInfo();

            if (KnownDataSets.ContainsKey(svcName))
            {
                result.SvcName = svcName;
                KnownDataSets.TryGetValue(svcName, out var dataSets);
                if (dataSets != null && dataSets.ContainsKey(dataSetName))
                {
                    result.DataSetName = dataSetName;
                    dataSets.TryGetValue(svcName, out var tables);

                    if (tables != null && tables.ContainsKey(tableName))
                    {
                        result.TableName = tableName;
                        tables.TryGetValue(tableName, out result.Fields);
                    }
                }
            }
            else
            {
                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence,
                    new AppDomainSetup
                    {
                        ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location)
                    });
                try
                {
                    var loader =
                        (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName,
                            typeof(AssemblyLoader).FullName ?? string.Empty);

                    var assemblyLoadResult = loader.LoadAssembly(svcName, assemblyPath);

                    if (!assemblyLoadResult)
                        return result;

                    result.SvcName = svcName;
                    KnownDataSets.Add(svcName, loader.AllDataSetsInfo);
                    if (loader.AllDataSetsInfo != null && loader.AllDataSetsInfo.ContainsKey(dataSetName))
                    {
                        result.DataSetName = dataSetName;
                        if (loader.AllDataSetsInfo.TryGetValue(dataSetName, out var datatables))
                        {
                            result.TableName = tableName;
                            datatables.TryGetValue(tableName, out result.Fields);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }

            return result;
        }

        private static readonly object LockDataViewCollectionGetter = new object();
        private static IEnumerable<ReportItem> CollectDataViewInfo(string methodName)
        {
            var report = new List<ReportItem>();

            if (_formDataViews.Count > 0)
                return report;

            lock (LockDataViewCollectionGetter)
            {
                if (_formDataViews.Count > 0)
                    return report;

                var svcList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && (n.FileType == KineticContentType.Events
                            || n.FileType == KineticContentType.Patch
                            || n.FileType == KineticContentType.Layout)
                        && n.Name == "svc"
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .Select(n => n.PatchedValue)
                    .Distinct()
                    .ToArray();

                foreach (var svc in svcList)
                    LoadService(svc, _serverAssembliesPath);

                // collect dataview definitions
                var dataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.DataViews
                        && n.Parent == "dataviews" && n.Name == "id"
                        || n.FileType == KineticContentType.Events
                        && n.Parent == "param"
                        && n.Name == "result"
                        && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.JsonPath == TrimPathEnd(n.ParentPath, 1) + ".type"
                        && m.PatchedValue == "dataview-condition")
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    /*.GroupBy(n => n.PatchedValue)
                    .Select(n => n.Last())*/
                    .ToArray();

                // add all system dataViews
                foreach (var item in _systemDataViews)
                {
                    var newView = new KineticDataView
                    {
                        DataViewName = item,
                        IsLocal = true
                    };
                    _formDataViews.Add(newView);
                }

                // collect complete info (dataSet/dataTable/fields/additional fields) for every dataView
                foreach (var kineticDataView in dataViewList)
                {
                    if (kineticDataView.PatchedValue.IndexOfAny(new[] { '{', '}', '%' }) >= 0) // non-constant dataView name
                        continue;

                    var isDvCondition = false;
                    var originalDataViewName = kineticDataView;
                    if (kineticDataView.Name == "result") // temp. view for <dataview-condition>
                    {
                        isDvCondition = true;
                        // find original dataView and use it's properties
                        var originalName = _jsonPropertiesCollection
                            .FirstOrDefault(n =>
                                n.ItemType == JsonItemType.Property
                                && n.FullFileName == kineticDataView.FullFileName
                                && n.JsonPath == TrimPathEnd(kineticDataView.JsonPath, 1) + ".dataview").PatchedValue;

                        originalDataViewName = _jsonPropertiesCollection
                           .FirstOrDefault(n =>
                               n.ItemType == JsonItemType.Property
                               && n.FileType == KineticContentType.DataViews
                               && n.Parent == "dataviews" && n.Name == "id"
                               && n.PatchedValue == originalName) ?? kineticDataView;
                    }

                    var kineticTableName = _jsonPropertiesCollection
                        .FirstOrDefault(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.DataViews
                            && n.FullFileName == originalDataViewName.FullFileName
                            && n.JsonPath == originalDataViewName.JsonPath.Replace(".id", ".table")
                            && !string.IsNullOrEmpty(n.PatchedValue))?
                        .PatchedValue;

                    var kineticDataSetName = _jsonPropertiesCollection
                        .FirstOrDefault(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.DataViews
                            && n.FullFileName == originalDataViewName.FullFileName
                            && n.JsonPath == originalDataViewName.JsonPath.Replace(".id", ".datasetId")
                            && !string.IsNullOrEmpty(n.PatchedValue))?
                        .PatchedValue;

                    var srvDataSetName = _jsonPropertiesCollection
                        .FirstOrDefault(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.DataViews
                            && n.FullFileName == originalDataViewName.FullFileName
                            && n.JsonPath == originalDataViewName.JsonPath.Replace(".id", ".serverDataset")
                            && !string.IsNullOrEmpty(n.PatchedValue))?
                        .PatchedValue;

                    var srvTableName = _jsonPropertiesCollection
                        .FirstOrDefault(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.DataViews
                            && n.FullFileName == originalDataViewName.FullFileName
                            && n.JsonPath == originalDataViewName.JsonPath.Replace(".id", ".serverTable")
                            && !string.IsNullOrEmpty(n.PatchedValue))?
                        .PatchedValue;

                    var additionalColumnsList = _jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.DataViews
                            && n.FullFileName == originalDataViewName.FullFileName
                            && n.JsonPath == originalDataViewName.JsonPath.Replace(".id", ".additionalColumns")
                            && !string.IsNullOrEmpty(n.PatchedValue))?
                        .Select(n => n.PatchedValue)?
                        .ToList();

                    // real dataView with server source
                    if (!string.IsNullOrEmpty(kineticDataSetName)
                        && !string.IsNullOrEmpty(srvDataSetName)
                        && !string.IsNullOrEmpty(srvTableName))
                    {
                        var svcName = KnownDataSets?.FirstOrDefault(n => n.Value.ContainsKey(srvDataSetName)).Key;

                        var newView = new KineticDataView
                        {
                            SvcName = svcName,
                            ServerDataSetName = srvDataSetName,
                            LocalDataSetName = kineticDataSetName,
                            ServerDataTableName = srvTableName,
                            LocalDataTableName = kineticTableName,
                            DataViewName = kineticDataView.PatchedValue,
                            AdditionalFields = additionalColumnsList,
                            IsDataViewCondition = isDvCondition
                        };

                        var serverDataSet = KnownDataSets?
                            .Select(n => n.Value?.FirstOrDefault(m => m.Key == srvDataSetName))?
                            .FirstOrDefault(n => n?.Key == srvDataSetName);

                        if (serverDataSet?.Key == null)
                        {
                            var newReport = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = originalDataViewName.FullFileName,
                                Message =
                                    $"ServerDataSet \"{srvDataSetName}\" targeted in \"{originalDataViewName.PatchedValue}\" dataView is not found",
                                FileType = originalDataViewName.FileType.ToString(),
                                LineId = originalDataViewName.LineId.ToString(),
                                JsonPath = originalDataViewName.JsonPath,
                                LineNumber = originalDataViewName.SourceLineNumber.ToString(),
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = methodName
                            };
                            report.Add(newReport);
                        }

                        var serverTable = serverDataSet?.Value?.FirstOrDefault(n => n.Key == srvTableName);

                        //trying to get dataTable by name in any of the dataSets available
                        if (serverDataSet?.Key == null)
                        {
                            foreach (var ds in KnownDataSets)
                            {
                                serverTable = ds.Value.Select(n => n.Value?.FirstOrDefault(m => m.Key == srvDataSetName))?
                                .FirstOrDefault(n => n?.Key == srvDataSetName);
                                if (serverTable != null)
                                    break;
                            }
                        }

                        if (serverTable?.Key == null)
                        {
                            var newReport = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = originalDataViewName.FullFileName,
                                Message =
                                    $"ServerDataTable \"{srvTableName}\" targeted in \"{originalDataViewName.PatchedValue}\" dataView is not found",
                                FileType = originalDataViewName.FileType.ToString(),
                                LineId = originalDataViewName.LineId.ToString(),
                                JsonPath = originalDataViewName.JsonPath,
                                LineNumber = originalDataViewName.SourceLineNumber.ToString(),
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = methodName
                            };

                            report.Add(newReport);
                        }
                        else
                        {
                            if (serverTable.Value.Value != null)
                                newView.Fields.AddRange(serverTable.Value.Value);
                        }

                        var oldDv = _formDataViews.LastOrDefault(n => n.DataViewName == newView.DataViewName);
                        if (oldDv == null)
                        {
                            _formDataViews.Add(newView);
                        }
                        else if (oldDv.IsDataViewCondition && newView.IsDataViewCondition)
                        {
                            oldDv.DataViewName = newView.DataViewName;
                            oldDv.SvcName = newView.SvcName;
                            oldDv.ServerDataSetName = newView.ServerDataSetName;
                            oldDv.LocalDataSetName = newView.LocalDataSetName;
                            oldDv.ServerDataTableName = newView.ServerDataTableName;
                            oldDv.LocalDataTableName = newView.LocalDataTableName;
                            oldDv.Fields = newView.Fields;
                            oldDv.AdditionalFields = newView.AdditionalFields;
                            oldDv.IsLocal = newView.IsLocal;
                            oldDv.IsDataViewCondition = newView.IsDataViewCondition;
                        }
                    }
                    // local dataView with no server source
                    else
                    {
                        var newView = new KineticDataView
                        {
                            ServerDataSetName = srvDataSetName,
                            LocalDataSetName = kineticDataSetName,
                            ServerDataTableName = srvTableName,
                            LocalDataTableName = kineticTableName,
                            DataViewName = kineticDataView.PatchedValue,
                            AdditionalFields = additionalColumnsList,
                            IsLocal = true,
                            IsDataViewCondition = isDvCondition
                        };

                        var oldDv = _formDataViews.LastOrDefault(n => n.DataViewName == newView.DataViewName);
                        if (oldDv == null)
                        {
                            _formDataViews.Add(newView);
                        }
                        else if (oldDv.IsLocal)
                        {
                            oldDv.DataViewName = newView.DataViewName;
                            oldDv.SvcName = newView.SvcName;
                            oldDv.ServerDataSetName = newView.ServerDataSetName;
                            oldDv.LocalDataSetName = newView.LocalDataSetName;
                            oldDv.ServerDataTableName = newView.ServerDataTableName;
                            oldDv.LocalDataTableName = newView.LocalDataTableName;
                            oldDv.Fields.AddRange(newView.Fields);
                            oldDv.AdditionalFields.AddRange(newView.AdditionalFields);
                            oldDv.IsLocal = newView.IsLocal;
                            oldDv.IsDataViewCondition = newView.IsDataViewCondition;
                        }
                        else
                        {
                            var newReport = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = kineticDataView.FullFileName,
                                Message =
                                    $"Dataview \"{newView.DataViewName}\" is overriding \"{oldDv.DataViewName}\"",
                                FileType = kineticDataView.FileType.ToString(),
                                LineId = kineticDataView.LineId.ToString(),
                                JsonPath = kineticDataView.JsonPath,
                                LineNumber = kineticDataView.SourceLineNumber.ToString(),
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = methodName
                            };

                            report.Add(newReport);
                        }
                    }
                }

                // collect all fields created by <row-update> and <search-value-set> for system and local dataViews
                var fieldsList = new List<string[]>();

                var rowUpdateList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && !n.Shared
                        && n.Name == "type"
                        && n.PatchedValue == "row-update")
                    .ToArray();

                foreach (var item in rowUpdateList)
                {
                    fieldsList.AddRange(_jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == item.FullFileName
                            && n.JsonPath.StartsWith(item.ParentPath + ".param[")
                            && n.Name == "epBinding"
                            && n.PatchedValue.Contains('.'))
                        .Select(n => n.PatchedValue.Split('.')));
                }

                var searchValueSetList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && !n.Shared
                        && n.Name == "type"
                        && n.PatchedValue == "search-value-set")
                    .ToArray();

                foreach (var item in searchValueSetList)
                {
                    fieldsList.AddRange(_jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == item.FullFileName
                            && n.JsonPath == item.ParentPath + ".epBinding"
                            && n.PatchedValue.Contains('.'))
                        .Select(n => n.PatchedValue.Split('.')));
                }

                // if there is any grid with selection feature enabled then add "Selected" field to certain dataView
                var selectableGrids = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Layout
                        && n.Name == "epBinding"
                        && _jsonPropertiesCollection.Any(m =>
                            m.FullFileName == n.FullFileName
                            && m.ParentPath == n.ParentPath + ".selectionOptions"
                            && (m.Name == "showCheckBox" || m.Name == "showSelectAll")
                            && m.PatchedValue.ToLower() == "true"
                            )
                        && _jsonPropertiesCollection.Any(m =>
                            m.FullFileName == n.FullFileName
                            && m.JsonPath == TrimPathEnd(n.ParentPath, 2) + ".sourceTypeId"
                            && m.PatchedValue == "metafx-panel-card-grid")
                        )
                    .ToArray();

                foreach (var dvName in selectableGrids)
                {
                    var dv = _formDataViews.FirstOrDefault(n => n.DataViewName == dvName.PatchedValue);

                    var selectedField = _jsonPropertiesCollection
                    .FirstOrDefault(n =>
                        n.FullFileName == dvName.FullFileName
                        && n.ParentPath.StartsWith(dvName.ParentPath + ".columns")
                        && n.Name == "field"
                        && _jsonPropertiesCollection.Any(m =>
                            m.FullFileName == n.FullFileName
                            && m.JsonPath == n.ParentPath + ".selector")
                    )?.PatchedValue ?? "Selected";

                    dv?.AdditionalFields.Add(selectedField);
                }

                var dataViewsGrouped = fieldsList.GroupBy(n => n[0]);

                foreach (var dataView in dataViewsGrouped)
                {
                    var dv = _formDataViews.FirstOrDefault(n => n.DataViewName == dataView.Key);

                    if (dv == null)
                        continue;

                    foreach (var field in dataView)
                    {
                        dv.AdditionalFields.Add(field[1]);
                    }

                    dv.AdditionalFields = dv.AdditionalFields.Distinct().ToList();

                    if (dv.IsLocal)
                        continue;

                    //remove any server fields
                    for (var i = 0; i < dv.AdditionalFields.Count; i++)
                    {
                        if (dv.Fields.Contains(dv.AdditionalFields[i]))
                        {
                            dv.AdditionalFields.RemoveAt(i);
                            i--;
                        }
                    }
                }
            }

            return report;
        }

        private enum ExpressionErrorCode
        {
            NoProblem,
            IncorrectOperator,
            IncompleteExpression,
            MissingValueField,
            MissingFieldEmbracement,
            OddFieldEmbracement,
            MissingOperator,
            InconsistentBrackets,
            IncorrectTypeComparison,
            FieldNotExists,
            DataViewNotExists,
            KeyWordNotExists,
            ContainsJsCode
        }

        private enum TokenType
        {
            TextField,
            ValueField,
            Operator,
            BracketOpen,
            BracketClose
        }

        private class ExpressionToken
        {
            public TokenType Type;
            public string Value;
            public ExpressionErrorCode ErrorCode;

            public string GetErrorsDescription()
            {
                return $"{ErrorCode} \"{Value}\"";
            }
        }

        private static IEnumerable<ExpressionToken> IsExpressionValid(string expression, bool sqlType = false, string dataView = "")
        {
            var tokens = new List<ExpressionToken>();
            var report = new List<ExpressionToken>();


            if (string.IsNullOrEmpty(expression))
                return report;

            var jsCodeSigns = new[] { "'.", ").", "in (", "in(" };
            if (jsCodeSigns.Any(n => expression.Contains(n)))
            {
                var newReport = new ExpressionToken()
                {
                    ErrorCode = ExpressionErrorCode.ContainsJsCode,
                    Type = TokenType.Operator,
                    Value = ""
                };
                report.Add(newReport);
            }

            expression = expression.Trim();

            var operators = new[] { "===", "!==", "==", "!=", "&&", "||", "<=", ">=", "<", ">", "!" };
            var postProcessOperators = new[] { "and", "not", "or" };
            var javaStyleOnly = new[] { "===", "!==", "==", "!=", "&&", "||", "!" };
            var sqlStyleOnly = new[] { "<>", "and", "not", "or" };

            if (sqlType)
                operators = new[] { "<>", "<=", ">=", "=", "<", ">" };

            var currentToken = new ExpressionToken
            {
                Value = "",
                Type = TokenType.ValueField
            };
            var currentTokenValue = new StringBuilder();
            char currentChar;
            var txtFlag = false;
            var jsFlag = false;
            for (var pos = 0; pos < expression.Length; pos++)
            {
                currentChar = expression[pos];

                // skip spaces except inside text fields
                if (!txtFlag && !jsFlag && currentChar == ' ')
                {
                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.ValueField
                    };
                    continue;
                }

                // start of text field
                if (!txtFlag && !jsFlag && currentChar == '\'')
                {
                    txtFlag = true;

                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.TextField
                    };
                    currentTokenValue.Append(currentChar);
                    continue;
                }

                // passing through text field
                if (txtFlag)
                {
                    // end of text field
                    if (currentChar == '\'')
                    {
                        currentTokenValue.Append(currentChar);
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                        txtFlag = false;

                        currentTokenValue.Clear();
                        currentToken = new ExpressionToken
                        {
                            Value = "",
                            Type = TokenType.ValueField
                        };
                    }
                    else
                    {
                        currentTokenValue.Append(currentChar);
                    }

                    continue;
                }

                // javascript insert start
                if (!jsFlag && currentChar == '#' && pos + 1 < expression.Length && expression[pos + 1] == '_')
                {
                    jsFlag = true;

                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.TextField
                    };
                    currentTokenValue.Append("#_");

                    pos++;

                    continue;
                }

                // passing through javascript
                if (jsFlag)
                {
                    // end of javascript insert
                    if (currentChar == '_' && pos + 1 < expression.Length && expression[pos + 1] == '#')
                    {
                        currentTokenValue.Append("_#");
                        pos++;
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                        jsFlag = false;

                        currentTokenValue.Clear();
                        currentToken = new ExpressionToken
                        {
                            Value = "",
                            Type = TokenType.ValueField
                        };
                    }
                    else
                    {
                        currentTokenValue.Append(currentChar);
                    }

                    continue;
                }

                // bracket opening
                if (currentChar == '(')
                {
                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = currentChar.ToString(),
                        Type = TokenType.BracketOpen
                    };
                    tokens.Add(currentToken);

                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.ValueField
                    };

                    continue;
                }

                // bracket closing
                if (currentChar == ')')
                {
                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = currentChar.ToString(),
                        Type = TokenType.BracketClose
                    };
                    tokens.Add(currentToken);

                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.ValueField
                    };

                    continue;
                }

                // operator
                if (operators.Any(n => n[0] == currentChar))
                {
                    if (currentTokenValue.Length > 0)
                    {
                        currentToken.Value = currentTokenValue.ToString();
                        tokens.Add(currentToken);
                    }

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.Operator
                    };

                    foreach (var operatorStr in operators)
                    {
                        if (pos + operatorStr.Length <= expression.Length)
                        {
                            var newValue = expression.Substring(pos, operatorStr.Length);
                            if (operatorStr == newValue)
                            {
                                currentTokenValue.Append(newValue);
                                pos += operatorStr.Length - 1;
                                break;
                            }
                        }
                    }

                    if (currentTokenValue.Length <= 0)
                    {
                        currentToken.ErrorCode = ExpressionErrorCode.IncorrectOperator;
                        report.Add(currentToken);
                    }

                    currentToken.Value = currentTokenValue.ToString();
                    tokens.Add(currentToken);

                    currentTokenValue.Clear();
                    currentToken = new ExpressionToken
                    {
                        Value = "",
                        Type = TokenType.ValueField
                    };

                    continue;
                }

                // field name
                currentTokenValue.Append(currentChar);
            }

            if (currentTokenValue.Length > 0)
            {
                currentToken.Value = currentTokenValue.ToString();
                tokens.Add(currentToken);
            }

            // mark all SQL operators 
            if (sqlType)
            {
                foreach (var t in tokens.Where(t => postProcessOperators.Contains(t.Value.ToLower())))
                    t.Type = TokenType.Operator;
            }

            // remove ContainsJsCode message if there is "#_ _#" JS code insert
            var jsErrorToken = report.FirstOrDefault(n => n.ErrorCode == ExpressionErrorCode.ContainsJsCode);
            if (jsErrorToken != null && tokens.Any(n => n.Type == TokenType.TextField && n.Value.StartsWith("#_")))
            {
                report.Remove(jsErrorToken);
            }

            // expression can't end with open bracket or operator or opened text/js field
            if (currentToken.Type == TokenType.BracketOpen
                || currentToken.Type == TokenType.Operator
                || txtFlag
                || jsFlag)
            {
                currentToken.ErrorCode = ExpressionErrorCode.IncompleteExpression;
                report.Add(currentToken);
            }

            // expression can't start with operator or closing bracket
            if (tokens[0].Type == TokenType.BracketClose
                || tokens[0].Type == TokenType.Operator)
            {
                if (!sqlType && tokens[0].Value != "!")
                {
                    tokens[0].ErrorCode = ExpressionErrorCode.IncompleteExpression;
                    report.Add(tokens[0]);
                }
                if (sqlType && tokens[0].Value.ToLower() != "not")
                {
                    tokens[0].ErrorCode = ExpressionErrorCode.IncompleteExpression;
                    report.Add(tokens[0]);
                }
            }

            // check order of fields and operations
            for (var i = 1; i < tokens.Count; i++)
            {
                var currentT = tokens[i];
                var previousT = tokens[i - 1];

                // no operators can come together
                if (currentT.Type == TokenType.Operator && previousT.Type == TokenType.Operator)
                {
                    if (sqlType && currentT.Value.ToLower() != "not")
                    {
                        if (javaStyleOnly.Contains(previousT.Value + currentT.Value))
                        {
                            currentT.ErrorCode = ExpressionErrorCode.IncorrectOperator;
                            report.Add(currentT);
                        }

                        currentT.ErrorCode = ExpressionErrorCode.MissingValueField;
                        report.Add(currentT);
                    }
                    else if (currentT.Value != "!")
                    {
                        if (sqlStyleOnly.Contains(previousT.Value + currentT.Value))
                        {
                            currentT.ErrorCode = ExpressionErrorCode.IncorrectOperator;
                            report.Add(currentT);
                        }

                        currentT.ErrorCode = ExpressionErrorCode.MissingValueField;
                        report.Add(currentT);
                    }
                }

                // no fields can come together
                if ((currentT.Type == TokenType.TextField || currentT.Type == TokenType.ValueField)
                    && (previousT.Type == TokenType.TextField || previousT.Type == TokenType.ValueField))
                {
                    if (!sqlType && currentT.Value[0] != '.')
                    {
                        currentT.ErrorCode = ExpressionErrorCode.MissingOperator;
                        report.Add(currentT);
                    }
                }

                // no field can come after closing bracket
                if (previousT.Type == TokenType.BracketClose &&
                    (currentT.Type == TokenType.TextField || currentT.Type == TokenType.ValueField))
                {
                    if (!sqlType && currentT.Value[0] != '.')
                    {
                        currentT.ErrorCode = ExpressionErrorCode.MissingOperator;
                        report.Add(currentT);
                    }
                }

                // no field can come before opening bracket
                if ((previousT.Type == TokenType.TextField || previousT.Type == TokenType.ValueField) &&
                    currentT.Type == TokenType.BracketOpen)
                {
                    if (!sqlType && !previousT.Value.Contains('.'))
                    {
                        currentT.ErrorCode = ExpressionErrorCode.MissingOperator;
                        report.Add(currentT);
                    }
                }
            }

            // all brackets should be closed - not really needed because of the next validation
            if (tokens.Count(n => n.Type == TokenType.BracketOpen) !=
                tokens.Count(n => n.Type == TokenType.BracketClose))
            {
                tokens[0].ErrorCode = ExpressionErrorCode.InconsistentBrackets;
                report.Add(tokens[0]);
            }

            // brackets order must be followed
            var counter = 0;
            foreach (var t in tokens)
            {
                if (t.Type == TokenType.BracketOpen)
                    counter++;
                else if (t.Type == TokenType.BracketClose)
                    counter--;

                if (counter < 0)
                {
                    tokens[0].ErrorCode = ExpressionErrorCode.InconsistentBrackets;
                    report.Add(tokens[0]);
                }
            }

            // both fields around the operator should be of same type (string/non-string)
            if (!sqlType)
            {
                var stringOperators = new[] { "===", "!==", "==", "!=", "<=", ">=", "<", ">", };

                for (var i = 1; i < tokens.Count - 1; i++)
                {
                    var currentT = tokens[i];
                    var previousT = tokens[i - 1];
                    var nextT = tokens[i + 1];
                    if (currentT.Type == TokenType.Operator && stringOperators.Contains(currentT.Value)
                        && (previousT.Type == TokenType.TextField || previousT.Type == TokenType.ValueField)
                        && (nextT.Type == TokenType.TextField || nextT.Type == TokenType.ValueField))
                    {
                        if (previousT.Type != nextT.Type)
                        {
                            currentT.ErrorCode = ExpressionErrorCode.IncorrectTypeComparison;
                            report.Add(currentT);
                        }
                    }
                }
            }

            // check dataFields existence
            foreach (var token in tokens.Where(n => n.Type == TokenType.ValueField && !IsNumeric(n.Value)))
            {
                var systemKeyWords = new[] { "true", "false" };
                if (systemKeyWords.Contains(token.Value.ToLower()))
                {
                    continue;
                }

                if (!sqlType)
                {

                    if (token.Value.Contains("%"))
                    {
                        if (token.Value.Contains('.'))
                        {
                            var systemMacro = new[] { "count%", "hasrow%", "row%", "haschanges%" };
                            var dv = token.Value.Split('.');
                            if (!systemMacro.Contains(dv[1].ToLower()))
                            {
                                token.ErrorCode = ExpressionErrorCode.KeyWordNotExists;
                                report.Add(token);
                            }

                            if (!DataViewExists(token.Value.Trim('%')))
                            {
                                token.ErrorCode = ExpressionErrorCode.DataViewNotExists;
                                report.Add(token);
                            }
                        }
                    }
                    else if (token.Value.Contains('.'))
                    {
                        if (token.Value.ToLower().StartsWith("context."))
                        {
                            continue;
                        }

                        if (!token.Value.StartsWith("{") || !token.Value.EndsWith("}"))
                        {
                            token.ErrorCode = ExpressionErrorCode.MissingFieldEmbracement;
                            report.Add(token);
                        }

                        if (!DataFieldExists(token.Value))
                        {
                            token.ErrorCode = ExpressionErrorCode.FieldNotExists;
                            report.Add(token);
                        }
                    }
                    else if (!DataViewExists(token.Value))
                    {
                        token.ErrorCode = ExpressionErrorCode.FieldNotExists;
                        report.Add(token);
                    }
                }
                else
                {
                    if (token.Value.ToLower() == "undefined")
                    {
                        continue;
                    }

                    var sqlToken = token.Value.Replace("[", "").Replace("]", "");
                    if (sqlToken.Contains(".$"))
                    {
                        var sqlKeyWords = new[] { "$rowcount" };
                        var dv = sqlToken.Split('.');
                        if (!sqlKeyWords.Contains(dv[1].ToLower()))
                        {
                            token.ErrorCode = ExpressionErrorCode.KeyWordNotExists;
                            report.Add(token);
                        }

                        if (!DataViewExists(sqlToken))
                        {
                            token.ErrorCode = ExpressionErrorCode.DataViewNotExists;
                            report.Add(token);
                        }
                    }
                    else if (sqlToken.Contains("%"))
                    {

                    }
                    else if (sqlToken.Contains('.'))
                    {
                        if (sqlToken.StartsWith("{") || sqlToken.EndsWith("}"))
                        {
                            token.ErrorCode = ExpressionErrorCode.OddFieldEmbracement;
                            report.Add(token);
                        }

                        if (!DataFieldExists(sqlToken))
                        {
                            token.ErrorCode = ExpressionErrorCode.FieldNotExists;
                            report.Add(token);
                        }
                    }
                    else if (!string.IsNullOrEmpty(dataView))
                    {
                        if (!DataFieldExists(dataView + "." + sqlToken))
                        {
                            token.ErrorCode = ExpressionErrorCode.FieldNotExists;
                            report.Add(token);
                        }
                    }
                }
            }

            // check field calls inside quotes of they are like '{dataVie.field}'
            if (!sqlType)
            {
                foreach (var token in tokens.Where(n => n.Type == TokenType.TextField && n.Value.Contains('.')))
                {
                    var value = token.Value.Trim('\'');
                    if (value.StartsWith("{") && value.EndsWith("}"))
                    {
                        if (!DataViewExists(value))
                        {
                            token.ErrorCode = ExpressionErrorCode.DataViewNotExists;
                            report.Add(token);
                        }
                        if (!DataFieldExists(value))
                        {
                            token.ErrorCode = ExpressionErrorCode.FieldNotExists;
                            report.Add(token);
                        }
                    }
                }
            }

            return report;
        }

        #endregion

        #region Validator methods

        private static IEnumerable<ReportItem> RunValidation(string methodName)
        {
            return _runValidationReportsCollection;
        }

        private static IEnumerable<ReportItem> DeserializeFile(string methodName)
        {
            return _deserializeFileReportsCollection;
        }

        private static IEnumerable<ReportItem> ParseJsonObject(string methodName)
        {
            return _parseJsonObjectReportsCollection;
        }

        private static IEnumerable<ReportItem> SchemaValidation(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            // validate every file with schema
            var report = new BlockingCollection<ReportItem>();
            Parallel.ForEach(_processedFilesList, file =>
            {
                var reportSet = ValidateFileSchema(_projectName, file.Key, file.Value).Result;

                foreach (var item in reportSet)
                    report.Add(item);
            });

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> RedundantFiles(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            // collect full list of files inside the project folder (not including shared)
            var fullFilesList = new List<string>();
            var foundFiles = Directory.GetFiles(_projectPath, _fileMask, SearchOption.AllDirectories);
            fullFilesList.AddRange(foundFiles);

            var report = fullFilesList
                .Where(file => file.IndexOf(_projectPath + "\\views\\", StringComparison.OrdinalIgnoreCase) < 0)
                .Where(file => !_processedFilesList.ContainsKey(file) && !Utilities.IsShared(file, _projectPath))
                .Select(file => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = file,
                    Message = "File is not used",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> ValidateFileChars(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var report = new List<ReportItem>();

            var propertyCollection = _jsonPropertiesCollection.Where(n =>
                !n.Shared
                && n.ItemType == JsonItemType.Property
                && !string.IsNullOrEmpty(n.Name)
                && !string.IsNullOrEmpty(n.Value));

            foreach (var item in propertyCollection)
            {
                var charPos = new StringBuilder();
                for (var i = 0; i < item.Name.Length; i++)
                {
                    if (item.Name[i] > 127)
                        charPos.Append($"\'0x{(int)item.Name[i]:x}\'[{i}] , ");
                }

                if (charPos.Length > 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        Message = $"Json property \"{item.Name}\" has non-ASCII chars at position(s): {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }

                charPos.Clear();
                for (var i = 0; i < item.Value.Length; i++)
                {
                    if (item.Value[i] > 127)
                        charPos.Append($"\'0x{(int)item.Value[i]:x}\'[{i}] , ");
                }

                if (charPos.Length > 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        Message = $"JSON property value \"{item.Value}\" has non-ASCII chars at position(s): {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> DuplicateIds(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var report = new List<ReportItem>();

            foreach (var file in _processedFilesList)
            {
                var propertyList = GetParsedFile(file.Key);

                var duplicateIdList = propertyList.Where(n =>
                        n.JsonPropertyType == JsonPropertyTypes.Property)
                    .GroupBy(n => n.Path)
                    .Where(n => n.Count() > 1)
                    .ToArray();

                if (!duplicateIdList.Any())
                    continue;

                foreach (var dup in duplicateIdList)
                {
                    var names = "";
                    foreach (var item in dup)
                    {
                        if (string.IsNullOrEmpty(names))
                            names += item.Value;
                        else
                            names += ", " + item.Value;
                    }

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = file.Key + _splitChar
                                                + file.Key,
                        JsonPath = "[1]" + dup.First().Path
                                         + _splitChar
                                         + "[2]" + dup.Last().Path,
                        StartPosition = dup.First().StartPosition + _splitChar.ToString()
                                                                  + dup.Last().StartPosition,
                        EndPosition = dup.First().EndPosition + _splitChar.ToString()
                                                              + dup.Last().EndPosition,
                        Message =
                            $"JSON file has duplicate property names [{names}]",
                        ValidationType = ValidationTypeEnum.Parse.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> DottedPropertyNames(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var ruleDataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !n.Shared
                    && n.Name.Contains('.')
                )
                .ToArray();

            var report = ruleDataViewList
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.FullFileName,
                    Message =
                        $"Property name should not contain \'.\' char",
                    FileType = n.FileType.ToString(),
                    LineId = n.LineId.ToString(),
                    JsonPath = n.JsonPath,
                    LineNumber = n.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> EmptyIdNames(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !n.Shared
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value))
                .ToArray();

            var report = emptyPatchList
                .Select(patchResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = patchResource.FullFileName,
                    FileType = patchResource.FileType.ToString(),
                    LineId = patchResource.LineId.ToString(),
                    JsonPath = patchResource.JsonPath,
                    LineNumber = patchResource.SourceLineNumber.ToString(),
                    Message = $"id \"{patchResource.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> RedundantPatches(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .ToArray();

            var report = patchList
                .Where(patchResource => !string.IsNullOrEmpty(patchResource.Value))
                .Where(patchResource => !_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType != KineticContentType.Patch
                        && n.Value.Contains("%" + patchResource.Value + "%")))
                .Select(patchResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = patchResource.FullFileName,
                    FileType = patchResource.FileType.ToString(),
                    LineId = patchResource.LineId.ToString(),
                    JsonPath = patchResource.JsonPath,
                    LineNumber = patchResource.SourceLineNumber.ToString(),
                    Message = $"Patch \"{patchResource.Value}\" is not used",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingPatches(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .ToArray();

            var report = _jsonPropertiesCollection
                .Where(property => property.ItemType == JsonItemType.Property)
                .Select(property => new { property, usedPatchList = GetPatchList(property.Value) })
                .SelectMany(n => n.usedPatchList, (m, patchItem) => new { k = m, patchItem })
                .Where(n => !string.IsNullOrEmpty(n.patchItem))
                .Where(n => !_systemMacros.Contains(n.patchItem)
                            && patchList.All(m => m.Value != n.patchItem.Trim('%')))
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.k.property.FullFileName,
                    FileType = n.k.property.FileType.ToString(),
                    LineId = n.k.property.LineId.ToString(),
                    JsonPath = n.k.property.JsonPath,
                    LineNumber = n.k.property.SourceLineNumber.ToString(),
                    Message = $"Patch \"{n.patchItem}\" is not defined",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingPatches(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var duplicatePatchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicatePatchesList.Any())
                return report;

            foreach (var dup in duplicatePatchesList)
            {
                var patchDup = dup.ToArray();
                for (var i = 1; i < patchDup.Count(); i++)
                {
                    var oldValue = _jsonPropertiesCollection
                        .LastOrDefault(n => n.FileType == KineticContentType.Patch
                                            && n.FullFileName == patchDup[i - 1].FullFileName
                                            && n.ParentPath == patchDup[i - 1].ParentPath
                                            && n.Name == "value");

                    if (oldValue == null || oldValue.Shared)
                        continue;

                    var newValue = _jsonPropertiesCollection
                        .LastOrDefault(n => n.FileType == KineticContentType.Patch
                                            && n.FullFileName == patchDup[i].FullFileName
                                            && n.ParentPath == patchDup[i].ParentPath
                                            && n.Name == "value");

                    if (newValue == null || string.IsNullOrEmpty(oldValue.Value) ||
                        oldValue.Value == newValue.Value)
                        continue;

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = patchDup[i - 1].FullFileName
                                       + _splitChar
                                       + patchDup[i].FullFileName,
                        Message =
                            $"Patch \"{patchDup[i - 1].Value}\" is overridden{Environment.NewLine} [\"{oldValue.Value}\" => \"{newValue.Value}\"]",
                        FileType = patchDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + patchDup[i].FileType,
                        LineId = patchDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + patchDup[i].LineId,
                        JsonPath = patchDup[i - 1].JsonPath
                                   + _splitChar
                                   + patchDup[i].JsonPath,
                        LineNumber = patchDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + patchDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> PossiblePatchValues(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var missedPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != KineticContentType.Patch
                    && _patchValues.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value))
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var item in missedPatchList)
            {
                var patchList = _patchValues
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Key);

                var patchDefinitions = "";
                foreach (var str in patchList)
                {
                    if (!string.IsNullOrEmpty(patchDefinitions))
                        patchDefinitions += ", ";

                    patchDefinitions += str;
                }

                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    LineNumber = item.SourceLineNumber.ToString(),
                    Message = $"Value \"{item.Value}\" can be replaced with patch(es): {patchDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> HardCodedStrings(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "message"
                    && !n.Value.Contains("{{strings.")
                    && !IsTableField(n.Value))
                .ToArray();

            var report = stringsList.Select(field => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = field.FullFileName,
                FileType = field.FileType.ToString(),
                LineId = field.LineId.ToString(),
                JsonPath = field.JsonPath,
                LineNumber = field.SourceLineNumber.ToString(),
                Message = $"String \"{field.Value}\" should be moved to strings.jsonc resource file",
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Note.ToString(),
                Source = methodName
            }).ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> PossibleStringsValues(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name))
                .ToArray();

            var missedStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != KineticContentType.Strings
                    && stringsList.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value))
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var item in missedStringsList)
            {
                var strList = stringsList
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Name);
                var stringDefinitions = "";

                foreach (var str in strList)
                {
                    if (!string.IsNullOrEmpty(stringDefinitions))
                        stringDefinitions += ", ";

                    stringDefinitions += str;
                }

                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item.FullFileName,
                    FileType = item.FileType.ToString(),
                    LineId = item.LineId.ToString(),
                    JsonPath = item.JsonPath,
                    LineNumber = item.SourceLineNumber.ToString(),
                    Message = $"String \"{item.Value}\" can be replaced with string variable(s): {stringDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> EmptyStringNames(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Name))
                .ToArray();

            var report = emptyStringsList
                .Select(stringResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    LineNumber = stringResource.SourceLineNumber.ToString(),
                    Message = $"String id \"{stringResource.Name}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> EmptyStringValues(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.PatchedValue))
                .ToArray();

            var report = emptyStringsList
                .Select(stringResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    LineNumber = stringResource.SourceLineNumber.ToString(),
                    Message = $"String id \"{stringResource.Name}\" value is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> RedundantStrings(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && !string.IsNullOrEmpty(n.Name))
                .ToArray();

            var report = stringsList
                .Where(stringResource => !string.IsNullOrEmpty(stringResource.Name)
                                         && !_jsonPropertiesCollection
                                             .Any(n =>
                                                 n.ItemType == JsonItemType.Property
                                                 && n.PatchedValue.Contains("strings." + stringResource.Name)))
                .Select(stringResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    LineNumber = stringResource.SourceLineNumber.ToString(),
                    Message = $"String \"{stringResource.Name}\" is not used",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingStrings(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && !string.IsNullOrEmpty(n.Name))
                .ToArray();

            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.Contains("{{strings."))
                .ToArray();


            var report = fieldsList
                .Select(field => new { field, strList = GetTableField(field.PatchedValue) })
                .SelectMany(n => n.strList, (m, str) => new { k = m, str })
                .Where(n =>
                    !string.IsNullOrEmpty(n.str)
                    && n.str.StartsWith("strings.")
                    && stringsList.All(m => m.Name != n.str.Replace("strings.", "")))
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.k.field.FullFileName,
                    FileType = n.k.field.FileType.ToString(),
                    LineId = n.k.field.LineId.ToString(),
                    JsonPath = n.k.field.JsonPath,
                    LineNumber = n.k.field.SourceLineNumber.ToString(),
                    Message = $"String \"{n.str}\" is not defined",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingStrings(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var duplicateStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && !string.IsNullOrEmpty(n.Name))
                .GroupBy(n => n.Name)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateStringsList.Any())
                return report;

            foreach (var dup in duplicateStringsList)
            {
                var strDup = dup.ToArray();
                for (var i = 1; i < strDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = strDup[i - 1].FullFileName
                                       + _splitChar
                                       + strDup[i].FullFileName,
                        Message =
                            $"String \"{strDup[i - 1].Name}\" is overridden{Environment.NewLine} [\"{strDup[i - 1].PatchedValue}\" => \"{strDup[i].PatchedValue}\"]",
                        FileType = strDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + strDup[i].FileType,
                        LineId = strDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + strDup[i].LineId,
                        JsonPath = strDup[i - 1].JsonPath
                                   + _splitChar
                                   + strDup[i].JsonPath,
                        LineNumber = strDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + strDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> EmptyEvents(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .ToArray();

            var report = emptyEventsList
                .Select(id => new
                {
                    id,
                    objectMembers = _jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == id.FullFileName
                            && n.ParentPath.Contains(id.ParentPath + ".actions[")
                            && n.Name == "type")
                })
                .Select(n => new { m = n, actionFound = n.objectMembers.Any() })
                .Where(n => !n.actionFound)
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.m.id.FullFileName,
                    Message = $"Event \"{n.m.id.PatchedValue}\" has no actions",
                    FileType = n.m.id.FileType.ToString(),
                    LineId = n.m.id.LineId.ToString(),
                    JsonPath = n.m.id.JsonPath,
                    LineNumber = n.m.id.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                })
                .ToList();

            var childEventNames = new[]
            {
                "onsuccess",
                "onfailure",
                "onerror",
                "onsinglematch",
                "onmultiplematch",
                "onnomatch",
                "onyes",
                "onno",
                "onok",
                "oncancel",
                "onabort",
                "onempty"
            };

            var emptyEventsList2 = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && childEventNames.Contains(n.Name.ToLower())
                    && !n.Shared)
                .ToArray();

            var report2 = emptyEventsList2
                .Select(id => new
                {
                    id,
                    objectMembers = _jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == id.FullFileName
                            && n.Name == "type"
                            && TrimArrayName(n.ParentPath) == id.JsonPath)
                })
                .Select(n => new { m = n, actionFound = n.objectMembers.Any() })
                .Where(n => !n.actionFound)
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.m.id.FullFileName,
                    Message = $"Event \"{n.m.id.Name}\" has no actions",
                    FileType = n.m.id.FileType.ToString(),
                    LineId = n.m.id.LineId.ToString(),
                    JsonPath = n.m.id.JsonPath,
                    LineNumber = n.m.id.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                })
                .ToArray();
            report.AddRange(report2);

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingEvents(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif

            var duplicateIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .GroupBy(n => n.PatchedValue)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateIdsList.Any())
                return report;

            foreach (var dup in duplicateIdsList)
            {
                // duplicate "id" within project (not including shared imports)
                var projectDuplicates = dup
                    .Where(n => !n.Shared)
                    .ToArray();

                if (projectDuplicates.Length > 1)
                    for (var i = projectDuplicates.Length - 1; i > 0; i--)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = projectDuplicates[i - 1].FullFileName
                                           + _splitChar
                                           + projectDuplicates[i].FullFileName,
                            Message = $"Event \"{projectDuplicates[i - 1].PatchedValue}\" is overridden",
                            FileType = projectDuplicates[i - 1].FileType
                                       + _splitChar.ToString()
                                       + projectDuplicates[i].FileType,
                            LineId = projectDuplicates[i - 1].LineId
                                     + _splitChar.ToString()
                                     + projectDuplicates[i].LineId,
                            JsonPath = projectDuplicates[i - 1].JsonPath
                                       + _splitChar
                                       + projectDuplicates[i].JsonPath,
                            LineNumber = projectDuplicates[i - 1].SourceLineNumber.ToString()
                                         + _splitChar
                                         + projectDuplicates[i].SourceLineNumber,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }

                // overriding shared "id" (project override shared)
                var sharedDuplicates = dup.Where(n => n.Shared).ToArray();
                if (!sharedDuplicates.Any() || !projectDuplicates.Any())
                    continue;

                var actionFound = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == sharedDuplicates.Last().FullFileName
                        && n.ParentPath.Contains(sharedDuplicates.Last().ParentPath + ".actions["))
                    .Any(n => n.Name == "type");

                if (actionFound)
                {
                    //non-empty shared method override
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = sharedDuplicates.Last().FullFileName
                                       + _splitChar
                                       + projectDuplicates.Last().FullFileName,
                        Message = $"Shared event \"{sharedDuplicates.Last().PatchedValue}\" is overridden",
                        FileType = sharedDuplicates.Last().FileType
                                   + _splitChar.ToString()
                                   + projectDuplicates.Last().FileType,
                        LineId = sharedDuplicates.Last().LineId
                                 + _splitChar.ToString()
                                 + projectDuplicates.Last().LineId,
                        JsonPath = sharedDuplicates.Last().JsonPath
                                   + _splitChar
                                   + projectDuplicates.Last().JsonPath,
                        LineNumber = sharedDuplicates.Last().SourceLineNumber.ToString()
                                     + _splitChar
                                     + projectDuplicates.Last().SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> RedundantEvents(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var possibleEvents = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .Select(n => "GetNew" + n.PatchedValue)
                .ToArray();

            var eventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && !n.Shared
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.IndexOfAny(new[] { '%', '{', '}' }) < 0
                    && !_jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FileType == KineticContentType.Events
                            && m.FullFileName == n.FullFileName
                            && m.ParentPath == n.ParentPath + ".trigger")
                        && !possibleEvents.Contains(n.PatchedValue))
                .ToArray();

            // call to event id: <event-next> -> "value" and "iterativeEvent"
            var eventCallList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    &&
                    (
                    (n.FileType == KineticContentType.Events
                    && (n.Name == "value"
                        && _jsonPropertiesCollection
                            .Any(m =>
                                m.ItemType == JsonItemType.Property
                                && m.FileType == KineticContentType.Events
                                && m.FullFileName == n.FullFileName
                                && m.ParentPath == n.ParentPath
                                && m.Name == "type"
                                && m.PatchedValue == "event-next")
                        || n.Name == "iterativeEvent"))


                        || (n.FileType == KineticContentType.DataViews
                        && n.Name == "eventID")
                        )
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.IndexOfAny(new[] { '%', '{', '}' }) < 0)
                .ToArray();

            var nonUsedEvent = eventsList
                .Where(n => eventCallList.All(m => m.PatchedValue != n.PatchedValue))
                .ToArray();

            var report = nonUsedEvent
                .Select(eventItem => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = eventItem.FullFileName,
                    FileType = eventItem.FileType.ToString(),
                    LineId = eventItem.LineId.ToString(),
                    JsonPath = eventItem.JsonPath,
                    LineNumber = eventItem.SourceLineNumber.ToString(),
                    Message = $"Event id \"{eventItem.PatchedValue}\" is not used",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingEvents(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var eventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.IndexOfAny(new[] { '%', '{', '}' }) < 0)
                .ToArray();

            // call to event id: <event-next> -> "value" and "iterativeEvent"
            var eventCallList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    &&
                    (
                    (n.FileType == KineticContentType.Events
                    && (n.Name == "value"
                        && _jsonPropertiesCollection
                            .Any(m =>
                                m.ItemType == JsonItemType.Property
                                && m.FileType == KineticContentType.Events
                                && m.FullFileName == n.FullFileName
                                && m.ParentPath == n.ParentPath
                                && m.Name == "type"
                                && m.PatchedValue == "event-next")
                        || n.Name == "iterativeEvent"))


                        || (n.FileType == KineticContentType.DataViews
                        && n.Name == "eventID")
                        )
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.IndexOfAny(new[] { '%', '{', '}' }) < 0)
                .ToArray();

            var nonExistEvent = eventCallList.Where(n => eventsList.All(m => m.PatchedValue != n.PatchedValue));

            var report = nonExistEvent
                .Select(eventId => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = eventId.FullFileName,
                    FileType = eventId.FileType.ToString(),
                    LineId = eventId.LineId.ToString(),
                    JsonPath = eventId.JsonPath,
                    LineNumber = eventId.SourceLineNumber.ToString(),
                    Message = $"Event id \"{eventId.PatchedValue}\" is not defined",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        // rework - not all cases managed
        private static IEnumerable<ReportItem> RedundantDataViews(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var dataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.DataViews
                        && n.Parent == "dataviews" && n.Name == "id"
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .ToList();

            var conditionDataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.FileType == KineticContentType.Events
                        && n.Parent == "param"
                        && n.Name == "result"
                        && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.JsonPath == TrimPathEnd(n.ParentPath, 1) + ".type"
                        && m.PatchedValue == "dataview-condition")
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .GroupBy(n => n.PatchedValue)
                    .Select(n => n.First())
                    .ToArray();

            dataViewList.AddRange(conditionDataViewList);

            var report = dataViewList
                .Where(viewResource => !_jsonPropertiesCollection
                    .Any(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != KineticContentType.DataViews
                    && n.PatchedValue.Contains(viewResource.PatchedValue))
                    && !_jsonPropertiesCollection
                    .Any(n =>
                    n.FullFileName == viewResource.FullFileName
                    && n.JsonPath == viewResource.ParentPath + ".serverDataset"
                    && n.PatchedValue == "DynamicMetadata")
                    )
                .Select(viewResource => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = viewResource.FullFileName,
                    FileType = viewResource.FileType.ToString(),
                    LineId = viewResource.LineId.ToString(),
                    JsonPath = viewResource.JsonPath,
                    LineNumber = viewResource.SourceLineNumber.ToString(),
                    Message = $"DataView \"{viewResource.PatchedValue}\" is not used",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingDataViews(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var dataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.DataViews
                        && n.Parent == "dataviews" && n.Name == "id"
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .ToList();

            var conditionDataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.FileType == KineticContentType.Events
                        && n.Parent == "param"
                        && n.Name == "result"
                        && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.JsonPath == TrimPathEnd(n.ParentPath, 1) + ".type"
                        && m.PatchedValue == "dataview-condition")
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .GroupBy(n => n.PatchedValue)
                    .Select(n => n.First())
                    .ToArray();

            dataViewList.AddRange(conditionDataViewList);

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataViewsList = new List<string>();

                if (property.FileType == KineticContentType.Events
                    && property.Parent == "trigger"
                    && property.Name == "target"
                    && !string.IsNullOrEmpty(property.PatchedValue)
                    && property.PatchedValue.Contains('.')
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FullFileName == property.FullFileName
                            && m.ParentPath == property.ParentPath
                            && m.Name == "type"
                            && m.PatchedValue == "EpBinding"))
                {
                    usedDataViewsList = GetTableField(property.PatchedValue);
                }
                else if (property.FileType == KineticContentType.Events
                         && property.Parent == "trigger"
                         && property.Name == "target"
                         && !string.IsNullOrEmpty(property.PatchedValue)
                         && !property.PatchedValue.Contains('.')
                         && _jsonPropertiesCollection
                             .Any(m =>
                                 m.ItemType == JsonItemType.Property
                                 && m.FullFileName == property.FullFileName
                                 && m.ParentPath == property.ParentPath
                                 && m.Name == "type"
                                 && m.PatchedValue == "DataView"))
                {
                    usedDataViewsList.Add(property.PatchedValue + ".");
                }
                else if (property.Name == "epBinding" && !string.IsNullOrEmpty(property.PatchedValue))
                {
                    if (property.PatchedValue.Contains('.'))
                        usedDataViewsList = GetTableField(property.PatchedValue);
                    else
                        usedDataViewsList.Add(property.PatchedValue + ".");
                }
                else if (property.FileType == KineticContentType.Rules
                         && property.Parent == "rules"
                         && (property.Name == "dataView" || property.Name == "targetDataView")
                         && !string.IsNullOrEmpty(property.PatchedValue))
                {
                    usedDataViewsList.Add(property.PatchedValue + ".");
                }
                else if (!string.IsNullOrEmpty(property.PatchedValue))
                {
                    usedDataViewsList = GetTableField(property.PatchedValue);
                }

                report.AddRange(usedDataViewsList
                    .Where(dataViewItem => !string.IsNullOrEmpty(dataViewItem))
                    .Select(dataViewItem => dataViewItem.Substring(0, dataViewItem.IndexOf('.')))
                    .Where(viewName =>
                        !_systemDataViews.Contains(viewName)
                        && !viewName.StartsWith("%")
                        && dataViewList.All(n => n.PatchedValue != viewName))
                    .Select(viewName => new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = property.FullFileName,
                        FileType = property.FileType.ToString(),
                        LineId = property.LineId.ToString(),
                        JsonPath = property.JsonPath,
                        LineNumber = property.SourceLineNumber.ToString(),
                        Message = $"DataView \"{viewName}\" is not defined",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    }));
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingDataViews(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && n.Parent == "dataviews" && n.Name == "id"
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .ToList();

            var conditionDataViewList = _jsonPropertiesCollection
                    .Where(n =>
                        n.FileType == KineticContentType.Events
                        && n.Parent == "param"
                        && n.Name == "result"
                        && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.JsonPath == TrimPathEnd(n.ParentPath, 1) + ".type"
                        && m.PatchedValue == "dataview-condition")
                        && !string.IsNullOrEmpty(n.PatchedValue))
                    .GroupBy(n => n.PatchedValue)
                    .Select(n => n.First())
                    .ToArray();

            dataViewList.AddRange(conditionDataViewList);
            var duplicateViewsList = dataViewList
                .GroupBy(n => n.PatchedValue)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateViewsList.Any())
                return report;

            foreach (var dup in duplicateViewsList)
            {
                var viewDup = dup.ToArray();
                for (var i = 1; i < viewDup.Count(); i++)
                {
                    var severity = ImportanceEnum.Error;
                    if (viewDup[i - 1].Shared)
                        severity = ImportanceEnum.Warning;

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = viewDup[i - 1].FullFileName
                                       + _splitChar
                                       + viewDup[i].FullFileName,
                        Message = $"DataView \"{viewDup[i - 1].PatchedValue}\" is overridden",
                        FileType = viewDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + viewDup[i].FileType,
                        LineId = viewDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + viewDup[i].LineId,
                        JsonPath = viewDup[i - 1].JsonPath
                                   + _splitChar
                                   + viewDup[i].JsonPath,
                        LineNumber = viewDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + viewDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = severity.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> CallNonExistingDataTables(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var dataTableList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "table"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.PatchedValue.IndexOfAny(new[] { '{', '}', '%' }) <= 0)
                .Select(n => n.PatchedValue)
                .Distinct()
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection.Where(n => n.ItemType == JsonItemType.Property))
            {
                var usedDataTable = "";

                if (property.FileType == KineticContentType.Events
                    && (
                    (property.Parent == "trigger"
                    && property.Name == "target"
                    && !string.IsNullOrEmpty(property.PatchedValue)
                    && !property.PatchedValue.Contains('.')
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FullFileName == property.FullFileName
                            && m.ParentPath == property.ParentPath
                            && m.Name == "type"
                            && m.PatchedValue == "DataTable"))
                    ||
                    (property.Name == "dataTable"
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FullFileName == property.FullFileName
                            && m.ParentPath == property.ParentPath
                            && m.Name == "type"
                            && (m.PatchedValue == "event-column-disable" || m.PatchedValue == "event-column-enable"))))
                    )
                {
                    usedDataTable = property.PatchedValue;
                }

                if (string.IsNullOrEmpty(usedDataTable)
                    || usedDataTable.IndexOfAny(new[] { '{', '}', '%' }) >= 0
                    || dataTableList.Contains(usedDataTable))
                {
                    continue;
                }

                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = property.FullFileName,
                    FileType = property.FileType.ToString(),
                    LineId = property.LineId.ToString(),
                    JsonPath = property.JsonPath,
                    LineNumber = property.SourceLineNumber.ToString(),
                    Message = $"DataTable \"{usedDataTable}\" is not defined",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> EmptyRules(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Name == "id"
                    && n.Parent == "rules"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .ToArray();

            var report = emptyEventsList
                .Select(id => new
                {
                    id,
                    objectMembers = _jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == id.FullFileName
                            && (
                            (n.ParentPath.Contains(id.ParentPath + ".actions[")
                            && n.Name == "action")
                            ||
                            (n.JsonPath == id.ParentPath + ".actions")
                            )
                            )
                })
                .Select(n => new { m = n, actionFound = n.objectMembers.Any() })
                .Where(n => !n.actionFound)
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.m.id.FullFileName,
                    Message = $"Rule \"{n.m.id.PatchedValue}\" has no actions",
                    FileType = n.m.id.FileType.ToString(),
                    LineId = n.m.id.LineId.ToString(),
                    JsonPath = n.m.id.JsonPath,
                    LineNumber = n.m.id.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> NoConditionRules(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Name == "id"
                    && n.Parent == "rules"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .ToArray();

            var report = emptyEventsList
                .Select(id => new
                {
                    id,
                    objectMembers = _jsonPropertiesCollection
                        .Where(n =>
                            n.ItemType == JsonItemType.Property
                            && n.FullFileName == id.FullFileName
                            && n.Name == "condition")
                })
                .Select(n => new { m = n, actionFound = n.objectMembers.Any() })
                .Where(n => !n.actionFound)
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.m.id.FullFileName,
                    Message = $"Rule \"{n.m.id.PatchedValue}\" has no condition",
                    FileType = n.m.id.FileType.ToString(),
                    LineId = n.m.id.LineId.ToString(),
                    JsonPath = n.m.id.JsonPath,
                    LineNumber = n.m.id.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingRules(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var duplicateRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .GroupBy(n => n.PatchedValue)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateRulesList.Any())
                return report;

            foreach (var dup in duplicateRulesList)
            {
                var ruleDup = dup.ToArray();
                for (var i = 1; i < ruleDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = ruleDup[i - 1].FullFileName
                                       + _splitChar
                                       + ruleDup[i].FullFileName,
                        Message = $"Rule \"{ruleDup[i - 1].PatchedValue}\" is overridden",
                        FileType = ruleDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + ruleDup[i].FileType,
                        LineId = ruleDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + ruleDup[i].LineId,
                        JsonPath = ruleDup[i - 1].JsonPath
                                   + _splitChar
                                   + ruleDup[i].JsonPath,
                        LineNumber = ruleDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + ruleDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> OverridingTools(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var duplicateToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.PatchedValue))
                .GroupBy(n => n.PatchedValue)
                .Where(n => n.Count() > 1)
                .ToArray();

            var report = new List<ReportItem>();
            if (!duplicateToolsList.Any())
                return report;

            foreach (var dup in duplicateToolsList)
            {
                var toolDup = dup.ToArray();
                for (var i = 1; i < toolDup.Count(); i++)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = toolDup[i - 1].FullFileName
                                       + _splitChar
                                       + toolDup[i].FullFileName,
                        Message = $"Tool \"{toolDup[i - 1].PatchedValue}\" is overridden",
                        FileType = toolDup[i - 1].FileType
                                   + _splitChar.ToString()
                                   + toolDup[i].FileType,
                        LineId = toolDup[i - 1].LineId
                                 + _splitChar.ToString()
                                 + toolDup[i].LineId,
                        JsonPath = toolDup[i - 1].JsonPath
                                   + _splitChar
                                   + toolDup[i].JsonPath,
                        LineNumber = toolDup[i - 1].SourceLineNumber.ToString()
                                     + _splitChar
                                     + toolDup[i].SourceLineNumber,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> MissingForms(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var formsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "view"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.Parent == "param"
                    && _jsonPropertiesCollection
                        .Any(m =>
                            n.ItemType == JsonItemType.Property
                            && m.FileType == KineticContentType.Events
                            && m.FullFileName == n.FullFileName
                            && m.Name == "type"
                            && m.PatchedValue == "app-open"
                            && m.ParentPath + ".param" == n.ParentPath))
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var form in formsList)
            {
                List<string> formFileList;

                switch (_folderType)
                {
                    case FolderType.Unknown:
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = form.FullFileName,
                            Message = "Folder type not recognized (Deployment/Repository/...)",
                            FileType = form.FileType.ToString(),
                            LineId = form.LineId.ToString(),
                            JsonPath = form.JsonPath,
                            LineNumber = form.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Warning.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);

                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\" + form.PatchedValue + "\\events.jsonc"
                        };
                        break;

                    case FolderType.Deployment:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\" + form.PatchedValue + "\\events.jsonc"
                        };
                        break;

                    case FolderType.IceRepository:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\..\\UIApps\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIProc\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIReports\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UITrackers\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIApps\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIProc\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIReports\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UITrackers\\" + form.PatchedValue + "\\events.jsonc"
                        };
                        break;

                    default:
                        formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\UIApps\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIProc\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIReports\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UITrackers\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIApps\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIProc\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIReports\\" + form.PatchedValue + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UITrackers\\" + form.PatchedValue + "\\events.jsonc"
                        };
                        break;
                }

                var fileFound = formFileList.Any(File.Exists);

                if (!fileFound)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing form \"{form.PatchedValue}\" (Could it be a WinForm?)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        LineNumber = form.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> MissingSearches(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var searchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "like"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.Parent == "searchOptions"
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FileType == KineticContentType.Events
                            && m.FullFileName == n.FullFileName
                            && m.Name == "searchForm"
                            && m.ParentPath == n.ParentPath))
                .ToArray();

            var report = new List<ReportItem>();
            foreach (var form in searchesList)
            {
                var formSubFolder = _jsonPropertiesCollection
                    .Where(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && !string.IsNullOrEmpty(m.PatchedValue)
                        && m.FullFileName == form.FullFileName
                        && m.FileType == KineticContentType.Events
                        && m.ParentPath == form.ParentPath)
                    .ToArray();

                if (!formSubFolder.Any())
                    continue;

                var searchName = form.PatchedValue
                                 + "\\"
                                 + formSubFolder.FirstOrDefault()?.PatchedValue;
                string searchFile;
                switch (_folderType)
                {
                    case FolderType.Unknown:
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = form.FullFileName,
                            Message = "Folder type not recognized (Deployment/Repository/...)",
                            FileType = form.FileType.ToString(),
                            LineId = form.LineId.ToString(),
                            JsonPath = form.JsonPath,
                            LineNumber = form.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Warning.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);

                        searchFile = _projectPath
                                     + "\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    case FolderType.Deployment:
                        searchFile = _projectPath
                                     + "\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    case FolderType.IceRepository:
                        searchFile = _projectPath
                                     + "\\..\\..\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;

                    default:
                        searchFile = _projectPath
                                     + "\\..\\..\\Shared\\search\\"
                                     + searchName
                                     + "\\search.jsonc";
                        break;
                }

                if (!File.Exists(searchFile))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing search \"{searchName}\"",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        LineNumber = form.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> JsCode(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.PatchedValue));

            var report = jsPatternsList.Select(jsCode => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = jsCode.FullFileName,
                Message = $"Property value contains JS code \"{jsCode.PatchedValue}\"",
                FileType = jsCode.FileType.ToString(),
                LineId = jsCode.LineId.ToString(),
                JsonPath = jsCode.JsonPath,
                LineNumber = jsCode.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Note.ToString(),
                Source = methodName
            }).ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> JsDataViewCount(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.PatchedValue)
                    && HasJsDvCount(n.PatchedValue));

            var report = jsPatternsList.Select(pattern => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = pattern.FullFileName,
                Message = $"JS code \"{pattern.PatchedValue}\" must be replaced to \"%DataView.count%\"",
                FileType = pattern.FileType.ToString(),
                LineId = pattern.LineId.ToString(),
                JsonPath = pattern.JsonPath,
                LineNumber = pattern.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Logic.ToString(),
                Severity = ImportanceEnum.Warning.ToString(),
                Source = methodName
            }).ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> IncorrectDvConditionViewName(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var dvConditionsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "type"
                    && n.PatchedValue == "dataview-condition");

            var report = new List<ReportItem>();
            foreach (var item in dvConditionsList)
            {
                var dvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "dataview").ToArray();

                var resultDvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "result").ToArray();

                if (dvName.Length != 1 || resultDvName.Length != 1)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "Incorrect dataview-condition definition",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }

                if (dvName[0].PatchedValue == resultDvName[0].PatchedValue)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            "Incorrect dataview-condition definition: \"dataview\" should be different from \"result\"",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = dvName[0].JsonPath,
                        LineNumber = dvName[0].SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> IncorrectRestCalls(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var restCallsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.PatchedValue == "rest-erp"
                    && n.Name == "type");

            var report = new List<ReportItem>();
            foreach (var item in restCallsList)
            {
                var serviceName = _jsonPropertiesCollection.FirstOrDefault(n => !n.Shared
                    && n.FullFileName == item.FullFileName
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.JsonPath == item.ParentPath + ".param.svc"
                    && !string.IsNullOrEmpty(n.PatchedValue));

                var svcMethodName = _jsonPropertiesCollection.FirstOrDefault(n => !n.Shared
                    && n.FullFileName == item.FullFileName
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.JsonPath == item.ParentPath + ".param.svcPath"
                    && !string.IsNullOrEmpty(n.PatchedValue));

                var methodParamsList = _jsonPropertiesCollection
                    .Where(n =>
                        !n.Shared
                        && n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Events
                        && (!GetParentName(n.ParentPath).StartsWith("params[")
                            && n.JsonPath.StartsWith(item.ParentPath + ".param.methodParameters")
                            && n.Name == "field"
                            || n.JsonPath.StartsWith(item.ParentPath + ".param.erpRestPostArgs")
                            && n.Name == "paramPath"))
                    .ToArray();

                if (serviceName == null || string.IsNullOrEmpty(serviceName.PatchedValue))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "REST service name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(serviceName.PatchedValue) || IsTableField(serviceName.PatchedValue) ||
                    HasJsCode(serviceName.PatchedValue))
                    continue;

                if (svcMethodName == null)
                    continue;

                LoadService(serviceName.PatchedValue, _serverAssembliesPath);

                var serverParams = GetRestMethod(serviceName.PatchedValue, svcMethodName.PatchedValue,
                    _serverAssembliesPath);

                if (string.IsNullOrEmpty(serverParams.SvcName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect REST service name: {serviceName.PatchedValue}",
                        FileType = serviceName.FileType.ToString(),
                        LineId = serviceName.LineId.ToString(),
                        JsonPath = serviceName.JsonPath,
                        LineNumber = serviceName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(svcMethodName.PatchedValue) || IsTableField(svcMethodName.PatchedValue) ||
                    HasJsCode(svcMethodName.PatchedValue))
                    continue;

                if (string.IsNullOrEmpty(svcMethodName.PatchedValue))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = "REST method name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        LineNumber = item.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (string.IsNullOrEmpty(serverParams.MethodName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            $"Incorrect REST service '{serviceName.PatchedValue}' method name: {svcMethodName.PatchedValue}",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.JsonPath,
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    continue;
                }

                // if failed to get params (no Epicor.ServceModel.dll found)
                if (serverParams.Parameters == null)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            $"Failed to retrieve REST service '{serviceName.PatchedValue}' method '{svcMethodName.PatchedValue}' parameters (Epicor.ServiceMode.dll not found?)",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.ParentPath,
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    continue;
                }

                var missingParams = new StringBuilder();
                if (methodParamsList.Any())
                {
                    foreach (var par in methodParamsList)
                    {
                        if (IsPatch(par.PatchedValue) || IsTableField(par.PatchedValue) || HasJsCode(par.PatchedValue))
                            continue;

                        if (!serverParams.Parameters.Contains(par.PatchedValue))
                            missingParams.Append(par.PatchedValue + ", ");
                    }

                    if (missingParams.Length > 0)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message =
                                $"Incorrect REST service '{serviceName.PatchedValue}' method '{svcMethodName.PatchedValue}' parameter names: {missingParams}",
                            FileType = methodParamsList.FirstOrDefault()?.FileType.ToString(),
                            LineId = methodParamsList.FirstOrDefault()?.LineId.ToString(),
                            JsonPath = methodParamsList.FirstOrDefault()?.ParentPath,
                            LineNumber = methodParamsList.FirstOrDefault()?.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }

                missingParams = new StringBuilder();
                if (!serverParams.Parameters.Any())
                    continue;

                var methodParamsTmp = methodParamsList.Select(t => t.PatchedValue).ToArray();
                foreach (var par in serverParams.Parameters.Where(t => t != "ds"))
                    if (!methodParamsTmp.Contains(par))
                        missingParams.Append(par + ", ");

                if (missingParams.Length > 0)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message =
                            $"Missing REST service '{serviceName.PatchedValue}' method '{svcMethodName.PatchedValue}' parameter names: {missingParams}",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.ParentPath + ".methodParameters",
                        LineNumber = svcMethodName.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> IncorrectFieldUsages(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var report = new List<ReportItem>();

            report.AddRange(CollectDataViewInfo(methodName));

            var localDataViews = _formDataViews.Where(n => n.IsLocal).Select(n => n.DataViewName).ToArray();

            //check every value field for a {dataView.field} pattern and check it certain combination exists
            foreach (var item in _jsonPropertiesCollection.Where(n =>
                n.ItemType == JsonItemType.Property
                && !string.IsNullOrEmpty(n.PatchedValue)
                && !n.Shared))
            {
                var fields = new List<string>();
                if (item.Name == "epBinding")
                {
                    var tokens = item.PatchedValue.Split(',');
                    fields.AddRange(tokens.Where(t => string.IsNullOrEmpty(t)));
                }
                else if (item.FileType == KineticContentType.Layout
                    && item.Name == "field"
                    && item.Parent == "columns"
                    && !string.IsNullOrEmpty(item.PatchedValue))
                {
                    var dv = _jsonPropertiesCollection.FirstOrDefault(n =>
                    n.FileType == KineticContentType.Layout
                    && n.FullFileName == item.FullFileName
                    && n.JsonPath == TrimPathEnd(item.ParentPath, 1) + ".epBinding")?.PatchedValue;
                    if (!string.IsNullOrEmpty(dv))
                        fields.Add(dv + "." + item.PatchedValue);
                }
                else
                {
                    fields.AddRange(GetTableField(item.PatchedValue).Where(n => n.IndexOfAny(new[] { '%', '{', '}' }) < 0));
                }

                if (fields.Count <= 0)
                    continue;

                if (string.IsNullOrEmpty(fields[0]))
                    continue;

                foreach (var tokens in fields.Select(field => field.Split('.')))
                {
                    var dvName = _formDataViews?.Where(n => n.DataViewName == tokens[0]).ToArray();
                    if (!dvName.Any())
                    {
                        var newReport = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"DataView \"{tokens[0]}\" is not defined",
                            FileType = item.FileType.ToString(),
                            LineId = item.LineId.ToString(),
                            JsonPath = item.JsonPath,
                            LineNumber = item.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(newReport);
                    }
                    else if (tokens.Length > 1
                             && !localDataViews.Contains(tokens[0])
                             && (!dvName.Any(n => n.AllFields.Contains(tokens[1]))))
                    {
                        var newReport = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"Field \"{tokens[1]}\" is not defined in dataView \"{tokens[0]}\"",
                            FileType = item.FileType.ToString(),
                            LineId = item.LineId.ToString(),
                            JsonPath = item.JsonPath,
                            LineNumber = item.SourceLineNumber.ToString(),
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(newReport);
                    }
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> UnEnclosedFieldValue(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var report = new List<ReportItem>();

            report.AddRange(CollectDataViewInfo(methodName));

            var rowUpdateList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && !n.Shared
                    && n.Name == "type"
                    && n.PatchedValue == "row-update")
                .ToArray();

            var valueList = new List<JsonProperty>();
            foreach (var item in rowUpdateList)
            {
                valueList.AddRange(_jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == item.FullFileName
                        && n.JsonPath.StartsWith(item.ParentPath)
                        && n.Name == "value"
                        && !string.IsNullOrEmpty(n.PatchedValue)
                        && n.PatchedValue.Contains('.')
                    ));
            }

            report.AddRange(valueList
                .Select(item => new
                {
                    item,
                    fields = GetTableField(item.PatchedValue, true)
                        .Where(n => n.Contains('.') && n.IndexOfAny(new[] { '%', '{', '}' }) < 0)
                        .ToArray()
                })
                .SelectMany(item1 => item1.fields, (item2, field) => new { item3 = item2, field })
                .Where(item4 => DataViewExists(item4.field))
                .Select(item5 => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = item5.item3.item.FullFileName,
                    Message = "Data-field \"" + item5.field + "\" in 'row-update'->'value' should be enclosed in '{}'",
                    FileType = item5.item3.item.FileType.ToString(),
                    LineId = item5.item3.item.LineId.ToString(),
                    JsonPath = item5.item3.item.JsonPath,
                    LineNumber = item5.item3.item.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                }));
#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> MissingLayoutIds(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id")
                .ToArray();

            var report = layoutIdList
                .Select(idItem => new
                {
                    idItem,
                    modelId = _jsonPropertiesCollection.Where(n =>
                            n.FullFileName == idItem.FullFileName
                            && n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.Layout
                            && n.ParentPath == idItem.ParentPath + ".model"
                            && n.Name == "id")
                        .ToArray()
                })
                .Where(n => !n.modelId.Any())
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.idItem.FullFileName,
                    Message = $"Layout control id=\"{n.idItem.PatchedValue}\" has no model id",
                    FileType = n.idItem.FileType.ToString(),
                    LineId = n.idItem.LineId.ToString(),
                    JsonPath = n.idItem.JsonPath,
                    LineNumber = n.idItem.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> IncorrectEventExpression(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var expressionList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Events
                    && n.Name == "expression"
                    && n.Parent == "param"
                    && _jsonPropertiesCollection.Any(m =>
                        m.ItemType == JsonItemType.Property
                        && m.FileType == KineticContentType.Events
                        && m.FullFileName == n.FullFileName
                        && m.JsonPath == n.ParentPath.Replace(".param", ".type")
                        && m.PatchedValue == "condition"))
                .ToArray();

            var report = new List<ReportItem>();
            report.AddRange(CollectDataViewInfo(methodName));

            var reportItems = expressionList
                .Select(expression => new { expression, result = IsExpressionValid(expression.PatchedValue) })
                .Where(n => n.result.All(m => m.ErrorCode != ExpressionErrorCode.NoProblem));

            foreach (var n in reportItems)
            {
                if (!n.result.Any())
                    continue;

                var errorList = new StringBuilder();
                foreach (var item in n.result)
                {
                    errorList.AppendLine(item.GetErrorsDescription());
                }

                var newReport = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.expression.FullFileName,
                    Message = $"Incorrect expression: \"{n.expression.PatchedValue}\"{Environment.NewLine}{errorList}",
                    FileType = n.expression.FileType.ToString(),
                    LineId = n.expression.LineId.ToString(),
                    JsonPath = n.expression.JsonPath,
                    LineNumber = n.expression.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };

                report.Add(newReport);
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> IncorrectRuleConditions(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var conditionList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && n.Name == "condition"
                    && n.Parent == "rules")
                .ToArray();

            var report = new List<ReportItem>();
            report.AddRange(CollectDataViewInfo(methodName));

            foreach (var condition in conditionList)
            {
                // get the dataView name for the rule
                var dvName = _jsonPropertiesCollection
                .FirstOrDefault(n =>
                    !n.Shared
                    && n.FullFileName == condition.FullFileName
                    && n.FileType == KineticContentType.Rules
                    && n.JsonPath == condition.ParentPath + ".dataView")?
                .PatchedValue ?? "";

                var r = IsExpressionValid(condition.PatchedValue, true, dvName).ToArray();
                if (r.Any())
                {
                    var errorList = new StringBuilder();
                    foreach (var item in r)
                    {
                        errorList.AppendLine(item.GetErrorsDescription());
                    }

                    var newReport = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = condition.FullFileName,
                        Message = $"Incorrect condition: \"{condition.PatchedValue}\"{Environment.NewLine}{errorList}",
                        FileType = condition.FileType.ToString(),
                        LineId = condition.LineId.ToString(),
                        JsonPath = condition.JsonPath,
                        LineNumber = condition.SourceLineNumber.ToString(),
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(newReport);
                }
            }

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> ProviderModelMixedWithClientFilter(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && !n.Shared
                    && n.Name == "clientFilter"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && _jsonPropertiesCollection
                    .Any(m =>
                    m.FullFileName == n.FullFileName
                    && m.JsonPath == TrimPathEnd(n.ParentPath, 1) + ".gridModel.providerModel.svc")
                    )
                .ToArray();

            var report = layoutIdList
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.FullFileName,
                    Message =
                        $"'providerModel' should not be used along with 'clientFilter'",
                    FileType = n.FileType.ToString(),
                    LineId = n.LineId.ToString(),
                    JsonPath = n.JsonPath,
                    LineNumber = n.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        private static IEnumerable<ReportItem> MultipleRowRuleForSameField(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var ruleDataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Rules
                    && !n.Shared
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    )
                .ToArray();

            var report = ruleDataViewList
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.FullFileName,
                    Message =
                        "Data field in row-update->value should be enclosed in '{}'",
                    FileType = n.FileType.ToString(),
                    LineId = n.LineId.ToString(),
                    JsonPath = n.JsonPath,
                    LineNumber = n.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        // is it really incorrect?
        private static IEnumerable<ReportItem> IncorrectLayoutIds(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id")
                .ToArray();

            var report = layoutIdList
                .Select(idItem => new
                {
                    idItem,
                    modelId = _jsonPropertiesCollection.Where(n =>
                            n.FullFileName == idItem.FullFileName
                            && n.ItemType == JsonItemType.Property
                            && n.FileType == KineticContentType.Layout
                            && n.ParentPath == idItem.ParentPath + ".model"
                            && n.Name == "id")
                        .ToArray()
                })
                .Where(n => n.modelId.Any())
                .Where(n => n.idItem.PatchedValue != n.modelId.First().PatchedValue)
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.idItem.FullFileName + _splitChar + n.modelId.First().FullFileName,
                    Message =
                        $"Layout control id=\"{n.idItem.PatchedValue}\" doesn't match model id=\"{n.modelId.First().PatchedValue}\"",
                    FileType = n.idItem.FileType.ToString() + _splitChar + n.modelId.First().FileType,
                    LineId = n.idItem.LineId.ToString() + _splitChar + n.modelId.First().LineId,
                    JsonPath = n.idItem.JsonPath + _splitChar + n.modelId.First().JsonPath,
                    LineNumber =
                        n.idItem.SourceLineNumber.ToString() + _splitChar +
                        n.modelId.First().SourceLineNumber,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        // is it really incorrect?
        private static IEnumerable<ReportItem> IncorrectTabIds(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var tabIdsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && !string.IsNullOrEmpty(n.PatchedValue)
                    && n.Name == "tabId").Select(n => n.PatchedValue)
                .ToArray();

            var tabStripsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.Name == "sourceTypeId"
                    && n.PatchedValue == "metafx-tabstrip")
                .ToArray();

            var report = tabStripsList
                .SelectMany(
                    tab => _jsonPropertiesCollection.Where(n =>
                        !n.Shared
                        && n.FullFileName == tab.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == KineticContentType.Layout
                        && n.JsonPath.Contains(tab.ParentPath + ".model.data")
                        && n.Name == "page"
                        && !string.IsNullOrEmpty(n.PatchedValue)), (tab, item) => new { tab, item })
                .Where(n => !tabIdsList.Contains(n.item.PatchedValue))
                .Select(n => new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = n.item.FullFileName,
                    Message = $"Inexistent tab link: {n.item.PatchedValue}",
                    FileType = n.item.FileType.ToString(),
                    LineId = n.item.LineId.ToString(),
                    JsonPath = n.item.JsonPath,
                    LineNumber = n.item.SourceLineNumber.ToString(),
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                })
                .ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        // is it really incorrect?
        private static IEnumerable<ReportItem> DuplicateGuiDs(string methodName)
        {
#if DEBUG
            var startTime = DateTime.Now;
#endif
            var item2 = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == KineticContentType.Layout
                    && n.Name == "guid").GroupBy(n => n.PatchedValue)
                .Where(n => n.Count() > 1)
                .ToArray();

            var guidList = item2;

            if (!guidList.Any())
                return new List<ReportItem>();

            var report = guidList.Select(dup => new ReportItem
            {
                ProjectName = _projectName,
                FullFileName = dup.FirstOrDefault()?.FullFileName,
                Message = $"Project has duplicate GUID \"{dup.FirstOrDefault()?.PatchedValue}\"",
                FileType = dup.FirstOrDefault()?.FileType.ToString(),
                LineId = dup.FirstOrDefault()?.LineId.ToString(),
                JsonPath = dup.FirstOrDefault()?.JsonPath,
                LineNumber = dup.FirstOrDefault()?.SourceLineNumber.ToString(),
                ValidationType = ValidationTypeEnum.Parse.ToString(),
                Severity = ImportanceEnum.Warning.ToString(),
                Source = methodName
            }).ToArray();

#if DEBUG
            Console.WriteLine($"{methodName} execution: {DateTime.Now.Subtract(startTime).TotalSeconds} sec.");
#endif
            return report;
        }

        #endregion
    }
}
