using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using NJsonSchema;
using NJsonSchema.Validation;

using static KineticValidator.JsonPathParser;

namespace KineticValidator
{
    public static class ProjectValidator
    {
        private class AssemblyLoader : MarshalByRefObject
        {
            private Assembly assembly = null;

            public bool LoadAssembly(string svcName, string assemblyPath)
            {
                var fileName = GetAssemblyContractName(svcName);

                if (string.IsNullOrEmpty(fileName))
                    return false;

                try
                {
                    this.assembly = Assembly.LoadFile(assemblyPath + "\\" + fileName);
                }
                catch
                {
                    return false;
                }

                return true;
            }

            private string GetAssemblyContractName(string svcName)
            {
                var fileName = "";
                var nameTokens = svcName.ToUpper().Split('.');
                if (nameTokens.Length != 3)
                {
                    return fileName;
                }

                // 1st token must be ["ERP","ICE"]
                if (!new string[] { "ERP", "ICE" }.Contains(nameTokens[0]))
                {
                    return fileName;
                }

                // 2nd token must be ["BO,"LIB","PROC","RPT","SEC","WEB"]
                if (!new string[] { "BO", "LIB", "PROC", "RPT", "SEC", "WEB" }.Contains(nameTokens[1]))
                {
                    return fileName;
                }

                nameTokens[2] = nameTokens[2].Replace("SVC", "");
                fileName = nameTokens[0] + ".Contracts." + nameTokens[1] + "." + nameTokens[2] + ".dll";

                return fileName;
            }

            public List<string> GetMethodsSafely(string svcName, string assemblyPath)
            {
                var contractName = GetAssemblyContractName(svcName);
                if (assembly == null || !assembly.GetName().Name.Equals(contractName, StringComparison.OrdinalIgnoreCase))
                {
                    var result = LoadAssembly(svcName, assemblyPath);
                    if (!result)
                    {
                        return null;
                    }
                }

                var methodsList = new List<string>();

                Type[] typesSafely;
                try
                {
                    typesSafely = this.assembly.GetTypes().Where(t => t != null && t.Name.EndsWith("SvcContract") && t.IsPublic).ToArray();
                }
                catch (ReflectionTypeLoadException ex)
                {
                    typesSafely = ex.Types.Where(t => t != null && t.Name.EndsWith("SvcContract") && t.IsPublic).ToArray();
                }

                try
                {
                    foreach (var type in typesSafely)
                    {
                        var methods = type.GetMethods();
                        methodsList.AddRange(methods.Select(t => t.Name).ToList());
                    }
                }
                catch (Exception ex)
                {
                    methodsList = null;
                }

                return methodsList;
            }

            public List<string> GetParamsSafely(string svcName, string methodName, string assemblyPath)
            {
                if (_noServiceModel)
                    return null;

                var contractName = GetAssemblyContractName(svcName);
                if (string.IsNullOrEmpty(contractName))
                    return null;

                if (assembly == null || !assembly.GetName().Name.Equals(contractName, StringComparison.OrdinalIgnoreCase))
                {
                    var result = LoadAssembly(svcName, assemblyPath);
                    if (!result)
                    {
                        return null;
                    }
                }

                List<string> paramList = null;

                MethodInfo method = null;
                IEnumerable<Type> typesSafely;
                try
                {
                    typesSafely = this.assembly.GetTypes().Where(t => t.Name.EndsWith("SvcContract") && t.IsPublic);
                }
                catch (ReflectionTypeLoadException ex)
                {
                    typesSafely = ex.Types.Where(t => t != null && t.Name.EndsWith("SvcContract") && t.IsPublic);
                }

                foreach (var type in typesSafely)
                {
                    method = type.GetMethod(methodName);
                    if (method != null)
                    {
                        try
                        {
                            ParameterInfo[] parameters = method.GetParameters();
                            paramList = new List<string>();
                            paramList = parameters.Where(t => !t.IsOut).Select(t => t.Name).ToList();
                        }
                        catch (Exception ex1)
                        {
                            _noServiceModel = true;
                        }

                        break;
                    }
                }

                return paramList;
            }
        }

        //constants from caller
        private static char _splitChar;
        private static string _schemaTag;
        private static string _backupSchemaExtension;
        private static string _fileMask;
        private static string _serverAssembliesPath;
        private static List<ContentTypeItem> _fileTypes;

        //setting flags from UI
        private static bool _ignoreHttpsError;
        private static bool _skipSchemaErrors;

        //settings from .config
        private static string[] _systemMacros;
        private static string[] _systemDataViews;
        private static List<ValidationErrorKind> _suppressSchemaErrors;

        //project folder related settings
        private static string _projectName;
        private static string _projectPath;
        private static FolderType _folderType;

        //only set
        private static Dictionary<string, string> _processedFilesList;
        private static List<JsonProperty> _jsonPropertiesCollection;
        private static List<ReportItem> _RunValidationReportsCollection;
        private static List<ReportItem> _DeserializeFileReportsCollection;
        private static List<ReportItem> _ParseJsonObjectReportsCollection;

        //only get
        internal static Dictionary<string, Func<string, List<ReportItem>>> _validatorsList { get; } = new Dictionary<string, Func<string, List<ReportItem>>>
        {
            {"File list validation", RunValidation},
            {"Serialization validation", DeserializeFile},
            {"JSON parser validation", ParseJsonObject},
            {"Schema validation", SchemaValidation},
            {"Redundant files", RedundantFiles},
            {"National characters", ValidateFileChars},
            {"Duplicate JSON property names", DuplicateIds},
            {"Empty patch names", EmptyPatchNames},
            {"Redundant patches", RedundantPatches},
            {"Non existing patches", CallNonExistingPatches},
            {"Overriding patches", OverridingPatches},
            {"Possible patches", PossiblePatchValues},
            {"Hard-coded message strings", HardCodedStrings},
            {"Possible strings", PossibleStringsValues},
            {"Apply patches", PatchAllFields},
            {"Empty string names", EmptyStringNames},
            {"Empty string values", EmptyStringValues},
            {"Redundant strings", RedundantStrings},
            {"Non existing strings", CallNonExistingStrings},
            {"Overriding strings", OverridingStrings},
            {"Empty event names", EmptyEventNames},
            {"Empty events", EmptyEvents},
            {"Overriding events", OverridingEvents},
            {"Redundant events", RedundantEvents},
            {"Non existing events", CallNonExistingEvents},
            {"Empty dataView names", EmptyDataViewNames},
            {"Redundant dataViews", RedundantDataViews},
            {"Non existing dataViews", CallNonExistingDataViews},
            {"Overriding dataViews", OverridingDataViews},
            {"Non existing dataTables", CallNonExistingDataTables},
            {"Empty rule names", EmptyRuleNames},
            {"Overriding rules", OverridingRules},
            {"Empty tool names", EmptyToolNames},
            {"Overriding tools", OverridingTools},
            {"Missing forms", MissingForms},
            {"Missing searches", MissingSearches},
            {"JavaScript code", JsCode},
            {"JS #_trans.dataView('DataView').count_#", JsDataViewCount},
            {"Missing layout id's", MissingLayoutIds},
            {"Incorrect layout id's", IncorrectLayoutIds},
            {"Incorrect dataview-condition", IncorrectDVContitionViewName},
            {"Incorrect tab links", IncorrectTabIds},
            {"Incorrect REST calls", IncorrectRestCalls},
        };

        //cached data
        private static Dictionary<string, string> _patchValues;
        private static Dictionary<string, List<ParsedProperty>> _parsedFiles;
        private static bool _noServiceModel;

        //global cache
        private static Dictionary<string, string> _schemaList = new Dictionary<string, string>();
        private static Dictionary<string, Dictionary<string, List<string>>> _knownServices = new Dictionary<string, Dictionary<string, List<string>>>();

        internal static void Initialize(ProcessConfiguration processConfiguration, ProjectConfiguration projectConfiguration, SeedData seedData, bool clearCashe)
        {
            if (processConfiguration != null)
            {
                _splitChar = processConfiguration.SplitChar;
                _schemaTag = processConfiguration.SchemaTag;
                _backupSchemaExtension = processConfiguration.BackupSchemaExtension;
                _fileMask = processConfiguration.FileMask;
                _fileTypes = processConfiguration.FileTypes;
                _ignoreHttpsError = processConfiguration.IgnoreHttpsError;
                _skipSchemaErrors = processConfiguration.SkipSchemaErrors;
                _systemMacros = processConfiguration.SystemMacros;
                _systemDataViews = processConfiguration.SystemDataViews;
                _suppressSchemaErrors = processConfiguration.SuppressSchemaErrors;
                _serverAssembliesPath = processConfiguration.ServerAssembliesPath;
            }

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
                _RunValidationReportsCollection = seedData.RunValidationReportsCollection;
                _DeserializeFileReportsCollection = seedData.DeserializeFileReportsCollection;
                _ParseJsonObjectReportsCollection = seedData.ParseJsonObjectReportsCollection;
            }

            _patchValues = new Dictionary<string, string>();

            if (clearCashe)
            {
                _parsedFiles = new Dictionary<string, List<ParsedProperty>>();
                _noServiceModel = false;

                //_schemaList = new Dictionary<string, string>();
                //_knownServices = new Dictionary<string, Dictionary<string, List<string>>>();
            }
        }

        #region Helping methods
        private struct Brackets
        {
            public int pos;
            public int level;
            public char bracket;
            public int number;
        }

        private static List<string> GetTableField(string field)
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
                var startCharNum = CountChars(token, startChar);
                var endCharNum = CountChars(token, endChar);
                if (startCharNum != 0 && endCharNum != 0 && startCharNum == endCharNum)
                {
                    var l = 0;
                    var n = 0;
                    var sequence = new List<Brackets>();
                    for (var i = 0; i < token.Length; i++)
                        if (token[i] == startChar)
                        {
                            l++;
                            sequence.Add(new Brackets
                            {
                                pos = i,
                                level = l,
                                bracket = startChar,
                                number = n
                            });
                            n++;
                        }
                        else if (token[i] == endChar)
                        {
                            sequence.Add(new Brackets
                            {
                                pos = i,
                                level = l,
                                bracket = endChar,
                                number = n
                            });
                            n++;
                            l--;
                        }

                    l = sequence.Max(brackets => brackets.level);
                    for (; l > 0; l--)
                    {
                        var s = sequence.Where(m => m.level == l).ToArray();
                        for (var i = 1; i < s.Length; i++)
                            if (s[i - 1].number + 1 == s[i].number && s[i - 1].bracket == startChar &&
                                s[i].bracket == endChar)
                            {
                                var str = token.Substring(s[i - 1].pos + 1, s[i].pos - s[i - 1].pos - 1);
                                if (str.Contains('.'))
                                    valueList.Add(str);
                            }
                    }
                }
            }


            return valueList;
        }

        private static int CountChars(string data, char countChar)
        {
            var c = 0;

            foreach (var ch in data)
                if (ch == countChar)
                    c++;

            return c;
        }

        private static List<string> GetPatchList(string field)
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

        private static bool IsTableField(string field)
        {
            Regex regex = new Regex(@"^{+\w+[.]+\w+}$");
            return regex.Match(field).Success;
        }

        private static bool IsPatch(string field)
        {
            Regex regex = new Regex(@"^%+\w+%$");
            return regex.Match(field).Success;
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

        private static async Task<List<ReportItem>> ValidateFileSchema(string _projectName, string fullFileName, string schemaUrl = "")
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
                    ProjectName = _projectName,
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
                        ProjectName = _projectName,
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
                    ProjectName = _projectName,
                    FullFileName = fullFileName,
                    Message = $"URL incorrect [{schemaUrl}]",
                    ValidationType = ValidationTypeEnum.Scheme.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = "ValidateFileSchema"
                };
                report.Add(reportItem);

                return report;
            }

            if (!_schemaList.ContainsKey(schemaUrl))
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
                        ProjectName = _projectName,
                        FullFileName = fullFileName,
                        Message = $"Schema download exception [{schemaUrl}]: {Utilities.ExceptionPrint(ex)}",
                        ValidationType = ValidationTypeEnum.Scheme.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = "ValidateFileSchema"
                    };
                    report.Add(reportItem);
                }

                _schemaList.Add(schemaUrl, schemaData);
            }

            var schemaText = _schemaList[schemaUrl];

            if (string.IsNullOrEmpty(schemaText))
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
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
                    ProjectName = _projectName,
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
                //_textLog.AppendLine($"{Environment.NewLine}{fullFileName} schema is empty{Environment.NewLine}");
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
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
                    ProjectName = _projectName,
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
                var errorText = SchemaErrorToString(fullFileName, error);
                var errorList = GetErrorList(_projectName, error);
                if (_skipSchemaErrors && _suppressSchemaErrors.Contains(error.Kind))
                {
                    //File.AppendAllText(LogFileName, errorText);
                }
                else
                {
                    foreach (var schemaError in errorList)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = fullFileName,
                            FileType = Utilities.GetFileTypeFromFileName(fullFileName, _fileTypes).ToString(),
                            LineId = schemaError.LineId,
                            JsonPath = schemaError.JsonPath.TrimStart('#', '/'),
                            Message = schemaError.Message,
                            ValidationType = ValidationTypeEnum.Scheme.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "ValidateFileSchema"
                        };
                        report.Add(reportItem);
                    }
                }
            }
            return report;
        }

        private static List<ReportItem> GetErrorList(string _projectName, ValidationError error)
        {
            var errorList = new List<ReportItem>();
            if (error is ChildSchemaValidationError subErrorCollection)
            {
                foreach (var subError in subErrorCollection.Errors)
                    foreach (var subErrorItem in subError.Value)
                    {
                        var report = new ReportItem
                        {
                            ProjectName = _projectName,
                            LineId = subErrorItem.LineNumber.ToString(),
                            JsonPath = subErrorItem.Path.TrimStart('#', '/'),
                            Message = subErrorItem.Kind.ToString(),
                            ValidationType = ValidationTypeEnum.Scheme.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = "GetErrorList"
                        };

                        errorList.Add(report);
                    }
            }
            else
            {
                var report = new ReportItem
                {
                    ProjectName = _projectName,
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

            if (error is ChildSchemaValidationError subErrorCollection)
                foreach (var subError in subErrorCollection.Errors)
                    foreach (var subErrorItem in subError.Value)
                        errorText.AppendLine("\t"
                            + "- line #"
                            + subErrorItem.LineNumber
                            + " "
                            + subErrorItem.Kind
                            + ", path="
                            + subErrorItem.Path);

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
                {
                    ServicePointManager.ServerCertificateValidationCallback = (a, b, c, d) => true;
                }
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
                        }
                        catch (Exception ex)
                        {
                            //_textLog.AppendLine($"Can't find directory: {dirPath}{Environment.NewLine}{ex}");
                            return schemaData;
                        }

                        File.WriteAllText(localPath + _backupSchemaExtension, schemaData);
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

        private static List<ParsedProperty> GetParsedFile(string file)
        {
            var list = new List<ParsedProperty>();
            if (!_parsedFiles.ContainsKey(file))
            {
                string json;
                try
                {
                    json = File.ReadAllText(file);
                }
                catch
                {
                    return list;
                }

                list = ParseJsonPathsStr(json.Replace(' ', ' '), true);
                _parsedFiles.Add(file, list);
            }
            else
            {
                list = _parsedFiles[file];
            }

            return list;
        }

        internal struct RestCallInfo
        {
            public string svcName;
            public string methodName;
            public List<string> parameters;
        }

        internal static RestCallInfo GetRestCallParams(string svcName, string methodName, string assemblyPath)
        {
            var result = new RestCallInfo();

            if (_knownServices.ContainsKey(svcName))
            {
                result.svcName = svcName;
                _knownServices.TryGetValue(svcName, out var methods);
                if (methods.ContainsKey(methodName))
                {
                    result.methodName = methodName;
                    methods.TryGetValue(methodName, out result.parameters);
                }
            }
            else
            {
                List<string> paramList = new List<string>();

                var domain = AppDomain.CreateDomain(nameof(AssemblyLoader), AppDomain.CurrentDomain.Evidence, new AppDomainSetup
                {
                    ApplicationBase = Path.GetDirectoryName(typeof(AssemblyLoader).Assembly.Location),
                    PrivateBinPath = assemblyPath + "\\..\\Bin",
                });
                try
                {
                    var loader = (AssemblyLoader)domain.CreateInstanceAndUnwrap(typeof(AssemblyLoader).Assembly.FullName, typeof(AssemblyLoader).FullName);

                    var success = loader.LoadAssembly(svcName, assemblyPath);
                    if (!success)
                    {
                        return result;
                    }

                    result.svcName = svcName;
                    var methodsList = loader.GetMethodsSafely(svcName, assemblyPath);
                    var m = new Dictionary<string, List<string>>();
                    foreach (var item in methodsList)
                    {
                        List<string> paramsList = loader.GetParamsSafely(svcName, item, assemblyPath);
                        m.Add(item, paramsList);

                        if (item == methodName)
                        {
                            result.methodName = methodName;
                            result.parameters = paramsList;
                        }
                    }
                    _knownServices.Add(svcName, m);
                }
                catch (Exception ex)
                {

                }
                finally
                {
                    AppDomain.Unload(domain);
                }
            }
            return result;
        }
        #endregion

        #region Validator methods
        internal static List<ReportItem> RunValidation(string methodName)
        {
            return _RunValidationReportsCollection;
        }

        internal static List<ReportItem> DeserializeFile(string methodName)
        {
            return _DeserializeFileReportsCollection;
        }

        internal static List<ReportItem> ParseJsonObject(string methodName)
        {
            return _ParseJsonObjectReportsCollection;
        }

        internal static List<ReportItem> SchemaValidation(string methodName)
        {
            // validate every file with schema
            var totalFiles = _processedFilesList.Count;
            var i1 = 1;

            var report = new List<ReportItem>();
            foreach (var file in _processedFilesList)
            {
                //SetStatus($"Validation scheme: {file.Key} [{i1}/{totalFiles}]");
                var reportSet = ValidateFileSchema(_projectName, file.Key, file.Value);
                report.AddRange(reportSet.Result);
                i1++;
            }

            return report;
        }

        internal static List<ReportItem> RedundantFiles(string methodName)
        {
            // collect full list of files inside the project folder (not including shared)
            var fullFilesList = new List<string>();
            fullFilesList.AddRange(Directory.GetFiles(_projectPath, _fileMask, SearchOption.AllDirectories));

            var report = new List<ReportItem>();
            foreach (var file in fullFilesList)
            {
                if (file.IndexOf(_projectPath + "\\views\\", StringComparison.OrdinalIgnoreCase) >= 0)
                    continue;

                if (!_processedFilesList.ContainsKey(file)
                    && !Utilities.IsShared(file, _projectPath))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = file,
                        Message = "File is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Note.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        internal static List<ReportItem> DuplicateIds(string methodName)
        {
            var report = new List<ReportItem>();

            foreach (var file in _processedFilesList)
            {
                var propertyList = GetParsedFile(file.Key);

                var item2 = propertyList.Where(n =>
                    n.Type == PropertyType.Property)
                        .GroupBy(n => n.Path)
                        .Where(n => n.Count() > 1);

                var duplicateIdList = item2 as IGrouping<string, ParsedProperty>[] ?? item2.ToArray();
                if (duplicateIdList.Any())
                {
                    foreach (var dup in duplicateIdList)
                    {
                        string values = "";
                        foreach (var item in dup)
                        {
                            if (string.IsNullOrEmpty(values))
                            {
                                values += "\"" + item.Value + "\"";
                            }
                            else
                            {
                                values += ", \"" + item.Value + "\"";
                            }
                        }
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = file.Key,
                            JsonPath = dup.First().Path,
                            Message = $"JSON file has duplicate property names \"{dup.First().Name}\" with values: {values}",
                            ValidationType = ValidationTypeEnum.Parse.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            }

            return report;
        }

        internal static List<ReportItem> ValidateFileChars(string methodName)
        {
            var report = new List<ReportItem>();

            foreach (var item in _jsonPropertiesCollection)
            {
                if (item == null || item.ItemType != JsonItemType.Property || !string.IsNullOrEmpty(item.Value))
                {
                    continue;
                }

                var charNum = 1;
                var chars = new List<int>();
                foreach (var ch in item.Name)
                {
                    if (ch > 127)
                    {
                        chars.Add(charNum);
                    }

                    charNum++;
                }

                if (chars.Count > 0)
                {
                    var charPos = new StringBuilder();
                    foreach (var ch in chars)
                    {
                        if (charPos.Length > 0)
                        {
                            charPos.Append(", " + ch);
                        }
                        else
                        {
                            charPos.Append(ch);
                        }
                    }

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        Message = $"Json property \"{item.Name}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }

                charNum = 1;
                chars = new List<int>();
                foreach (var ch in item.Value)
                {
                    if (ch > 127)
                    {
                        chars.Add(charNum);
                    }

                    charNum++;
                }

                if (chars.Count > 0)
                {
                    var charPos = new StringBuilder();
                    foreach (var ch in chars)
                    {
                        if (charPos.Length > 0)
                        {
                            charPos.Append(", " + ch);
                        }
                        else
                        {
                            charPos.Append(ch);
                        }
                    }

                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        Message = $"JSON property value \"{item.Value}\" has non-ASCII chars at position(s) {charPos}",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }

            return report;
        }

        internal static List<ReportItem> EmptyStringNames(string methodName)
        {
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Name));

            var report = new List<ReportItem>();
            foreach (var stringResource in emptyStringsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    Message = $"String id \"{stringResource.Name}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

            return report;
        }

        internal static List<ReportItem> EmptyStringValues(string methodName)
        {
            var emptyStringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var stringResource in emptyStringsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = stringResource.FullFileName,
                    FileType = stringResource.FileType.ToString(),
                    LineId = stringResource.LineId.ToString(),
                    JsonPath = stringResource.JsonPath,
                    Message = $"String id \"{stringResource.Name}\" value is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }

            return report;
        }

        internal static List<ReportItem> RedundantStrings(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var report = new List<ReportItem>();
            foreach (var stringResource in stringsList)
                if (!string.IsNullOrEmpty(stringResource.Name)
                    && !_jsonPropertiesCollection.Any(n =>
                            n.ItemType == JsonItemType.Property
                            && n.Value.Contains("strings." + stringResource.Name)))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = stringResource.FullFileName,
                        FileType = stringResource.FileType.ToString(),
                        LineId = stringResource.LineId.ToString(),
                        JsonPath = stringResource.JsonPath,
                        Message = $"String \"{stringResource.Name}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            return report;
        }

        internal static List<ReportItem> CallNonExistingStrings(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Value.Contains("{{strings."));

            var report = new List<ReportItem>();
            foreach (var field in fieldsList)
            {
                var strList = GetTableField(field.Value);
                foreach (var str in strList)
                    if (!string.IsNullOrEmpty(str)
                        && str.StartsWith("strings.")
                        && !stringsList
                        .Any(n => n.Name == str.Replace("strings.", "")))
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = field.FullFileName,
                            FileType = field.FileType.ToString(),
                            LineId = field.LineId.ToString(),
                            JsonPath = field.JsonPath,
                            Message = $"String \"{str}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
            }
            return report;
        }

        internal static List<ReportItem> OverridingStrings(string methodName)
        {
            var duplicateStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name))
                .GroupBy(n => n.Name)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicateStringsList.Any())
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
                            Message = $"String \"{strDup[i - 1].Name}\" is overridden{Environment.NewLine} [\"{strDup[i - 1].Value}\" => \"{strDup[i].Value}\"]",
                            FileType = strDup[i - 1].FileType
                                       + _splitChar.ToString()
                                       + strDup[i].FileType,
                            LineId = strDup[i - 1].LineId
                                     + _splitChar.ToString()
                                     + strDup[i].LineId,
                            JsonPath = strDup[i - 1].JsonPath
                                       + _splitChar
                                       + strDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            return report;
        }

        internal static List<ReportItem> HardCodedStrings(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "message"
                    && !n.Value.Contains("{{strings.")
                    && !IsTableField(n.Value));

            var report = new List<ReportItem>();
            foreach (var field in stringsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = field.FullFileName,
                    FileType = field.FileType.ToString(),
                    LineId = field.LineId.ToString(),
                    JsonPath = field.JsonPath,
                    Message = $"String \"{field.Value}\" should be moved to strings.jsonc resource file",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> PossibleStringsValues(string methodName)
        {
            var stringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Strings
                    && n.Parent == "strings"
                    && !string.IsNullOrEmpty(n.Value)
                    && !string.IsNullOrEmpty(n.Name));

            var missedStringsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != JsoncContentType.Strings
                    && stringsList.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            var report = new List<ReportItem>();
            foreach (var item in missedStringsList)
            {
                var strlist = stringsList
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Name);
                var stringDefinitions = "";

                foreach (var str in strlist)
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
                    Message = $"String \"{item.Value}\" can be replaced with string variable(s): {stringDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> EmptyEventNames(string methodName)
        {
            var emptyIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var id in emptyIdsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = id.FullFileName,
                    FileType = id.FileType.ToString(),
                    LineId = id.LineId.ToString(),
                    JsonPath = id.JsonPath,
                    Message = $"Event id \"{id.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> EmptyEvents(string methodName)
        {
            var emptyEventsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var id in emptyEventsList)
            {
                var objectMembers = _jsonPropertiesCollection
                    .Where(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == id.FullFileName
                        && n.ParentPath.Contains(id.ParentPath + ".actions[")
                        && n.Name == "type");
                var actionFound = objectMembers.Any();

                if (!actionFound)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = id.FullFileName,
                        Message = $"Event \"{id.Value}\" has no actions",
                        FileType = id.FileType.ToString(),
                        LineId = id.LineId.ToString(),
                        JsonPath = id.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Note.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> RedundantEvents(string methodName)
        {
            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !n.Shared
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var id in idProjectList)
            {
                // check it's a trigger method called on change of smth.
                if (_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FullFileName == id.FullFileName
                        && n.ParentPath == id.ParentPath
                        && n.Name == "trigger"))
                    continue;

                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.Name != "id"
                        && n.Value == id.Value))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = id.FullFileName,
                        FileType = id.FileType.ToString(),
                        LineId = id.LineId.ToString(),
                        JsonPath = id.JsonPath,
                        Message = $"Event id \"{id.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> CallNonExistingEvents(string methodName)
        {
            var idProjectList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.Value));

            // call to non-existing "id" - for "value" && path+"type": "event-next", "iterativeEvent", 
            var fieldsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Name == "value"
                    && _jsonPropertiesCollection.Any(m =>
                        m.FullFileName == n.FullFileName
                        && m.ParentPath == n.ParentPath
                        && m.Name == "type"
                        && m.Value == "event-next")
                    || n.Name == "iterativeEvent")
                .Where(n =>
                    !n.Value.Contains('%')
                    && !n.Value.Contains('{')
                    && idProjectList.All(m => m.Value != n.Value));

            var report = new List<ReportItem>();
            foreach (var field in fieldsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = field.FullFileName,
                    FileType = field.FileType.ToString(),
                    LineId = field.LineId.ToString(),
                    JsonPath = field.JsonPath,
                    Message = $"Event id \"{field.Value}\" is not defined in the project",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> OverridingEvents(string methodName)
        {
            var duplicateIdsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "id"
                    && n.Parent == "events"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicateIdsList.Any())
            {
                foreach (var dup in duplicateIdsList)
                {
                    // duplicate "id" within project (not including shared imports)
                    var projectDuplicates = dup.Where(n => !n.Shared).ToList();
                    if (projectDuplicates.Count > 1)
                        for (var i = projectDuplicates.Count - 1; i > 0; i--)
                        {
                            var reportItem = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = projectDuplicates[i - 1].FullFileName
                                               + _splitChar
                                               + projectDuplicates[i].FullFileName,
                                Message = $"Event \"{projectDuplicates[i - 1].Value}\" is overridden",
                                FileType = projectDuplicates[i - 1].FileType
                                           + _splitChar.ToString()
                                           + projectDuplicates[i].FileType,
                                LineId = projectDuplicates[i - 1].LineId
                                         + _splitChar.ToString()
                                         + projectDuplicates[i].LineId,
                                JsonPath = projectDuplicates[i - 1].JsonPath
                                           + _splitChar
                                           + projectDuplicates[i].JsonPath,
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Error.ToString(),
                                Source = methodName
                            };
                            report.Add(reportItem);
                        }

                    // overriding shared "id" (project override shared)
                    var sharedDuplicates = dup.Where(n => n.Shared);
                    if (sharedDuplicates.Any() && projectDuplicates.Any())
                    {
                        var objectMembers = _jsonPropertiesCollection
                            .Where(n =>
                                n.ItemType == JsonItemType.Property
                                && n.FullFileName == sharedDuplicates.Last().FullFileName
                                && n.ParentPath.Contains(sharedDuplicates.Last().ParentPath + ".actions["));
                        var actionFound = objectMembers.Any(n => n.Name == "type");

                        if (actionFound)
                        {
                            //non-empty shared method override
                            var reportItem = new ReportItem
                            {
                                ProjectName = _projectName,
                                FullFileName = sharedDuplicates.Last().FullFileName
                                               + _splitChar
                                               + projectDuplicates.Last().FullFileName,
                                Message = $"Shared event \"{sharedDuplicates.Last().Value}\" is overridden",
                                FileType = sharedDuplicates.Last().FileType
                                           + _splitChar.ToString()
                                           + projectDuplicates.Last().FileType,
                                LineId = sharedDuplicates.Last().LineId
                                         + _splitChar.ToString()
                                         + projectDuplicates.Last().LineId,
                                JsonPath = sharedDuplicates.Last().JsonPath
                                           + _splitChar
                                           + projectDuplicates.Last().JsonPath,
                                ValidationType = ValidationTypeEnum.Logic.ToString(),
                                Severity = ImportanceEnum.Warning.ToString(),
                                Source = methodName
                            };
                            report.Add(reportItem);
                        }
                    }
                }
            }
            return report;
        }

        internal static List<ReportItem> EmptyPatchNames(string methodName)
        {
            var emptPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));
            var report = new List<ReportItem>();
            foreach (var patchResource in emptPatchList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = patchResource.FullFileName,
                    FileType = patchResource.FileType.ToString(),
                    LineId = patchResource.LineId.ToString(),
                    JsonPath = patchResource.JsonPath,
                    Message = $"Patch id \"{patchResource.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static Dictionary<string, string> CollectPatchValues()
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id");

            var patchValues = new Dictionary<string, string>();
            foreach (var item in patchList)
            {
                var newValue = _jsonPropertiesCollection
                    .LastOrDefault(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Patch
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
                var toPatch = patchValues.Where(n =>
                n.Value.Trim().StartsWith("%")
                && n.Value.Trim().EndsWith("%"));

                allReplaced = true;

                if (toPatch.Any())
                {
                    var replaceValues = new Dictionary<string, string>();
                    foreach (var item in toPatch)
                        foreach (var patch in patchValues)
                            if (patch.Key == item.Value)
                            {
                                replaceValues.Add(item.Key, patch.Value);
                                allReplaced = false;
                                break;
                            }

                    foreach (var r in replaceValues)
                        patchValues[r.Key] = r.Value;
                }
            } while (!allReplaced);

            return patchValues;
        }

        internal static List<ReportItem> PossiblePatchValues(string methodName)
        {
            if (_patchValues == null || _patchValues.Count == 0)
                _patchValues = CollectPatchValues();

            var missedPatchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType != JsoncContentType.Patch
                    && _patchValues.Any(m =>
                        !string.IsNullOrEmpty(m.Value)
                        && m.Value == n.Value));

            var report = new List<ReportItem>();
            foreach (var item in missedPatchList)
            {
                var patchlist = _patchValues
                    .Where(m => m.Value == item.Value)
                    .Select(n => n.Key);
                var patchDefinitions = "";
                foreach (var str in patchlist)
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
                    Message = $"Value \"{item.Value}\" can be replaced with patch(es): {patchDefinitions}",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> PatchAllFields(string methodName)
        {
            if (_patchValues == null || _patchValues.Count == 0)
            {
                _patchValues = CollectPatchValues();
            }

            var valuesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.Value.Contains('%'));

            if (valuesList.Any())
            {
                foreach (var item in valuesList)
                {
                    foreach (var patch in _patchValues)
                    {
                        item.Value = item.Value.Replace(patch.Key, patch.Value);
                    }
                }
            }

            return new List<ReportItem>();
        }

        internal static List<ReportItem> RedundantPatches(string methodName)
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && !n.Shared
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));
            var report = new List<ReportItem>();
            foreach (var patchResource in patchList)
            {
                if (string.IsNullOrEmpty(patchResource.Value))
                    continue;

                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType != JsoncContentType.Patch
                        && n.Value.Contains("%" + patchResource.Value + "%")))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = patchResource.FullFileName,
                        FileType = patchResource.FileType.ToString(),
                        LineId = patchResource.LineId.ToString(),
                        JsonPath = patchResource.JsonPath,
                        Message = $"Patch \"{patchResource.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> CallNonExistingPatches(string methodName)
        {
            var patchList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedPatchList = GetPatchList(property.Value);
                foreach (var patchItem in usedPatchList)
                {
                    if (string.IsNullOrEmpty(patchItem))
                        continue;

                    if (!_systemMacros.Contains(patchItem)
                        && !patchList.Any(n => n.Value == patchItem.Trim('%')))
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = property.FullFileName,
                            FileType = property.FileType.ToString(),
                            LineId = property.LineId.ToString(),
                            JsonPath = property.JsonPath,
                            Message = $"Patch \"{patchItem}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            }
            return report;
        }

        internal static List<ReportItem> OverridingPatches(string methodName)
        {
            var duplicatePatchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Patch
                    && n.Parent == "patch"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicatePatchesList.Any())
                foreach (var dup in duplicatePatchesList)
                {
                    var patchDup = dup.ToArray();
                    for (var i = 1; i < patchDup.Count(); i++)
                    {
                        var oldValue = _jsonPropertiesCollection
                            .LastOrDefault(n => n.FileType == JsoncContentType.Patch
                                                && n.FullFileName == patchDup[i - 1].FullFileName
                                                && n.ParentPath == patchDup[i - 1].ParentPath
                                                && n.Name == "value");
                        if (oldValue.Shared)
                            continue;

                        var newValue = _jsonPropertiesCollection
                            .LastOrDefault(n => n.FileType == JsoncContentType.Patch
                                                && n.FullFileName == patchDup[i].FullFileName
                                                && n.ParentPath == patchDup[i].ParentPath
                                                && n.Name == "value");

                        if (string.IsNullOrEmpty(oldValue.Value) || oldValue.Value == newValue.Value)
                            continue;

                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = patchDup[i - 1].FullFileName
                                           + _splitChar
                                           + patchDup[i].FullFileName,
                            Message = $"Patch \"{patchDup[i - 1].Value}\" is overridden{Environment.NewLine} [\"{oldValue.Value}\" => \"{newValue.Value}\"]",
                            FileType = patchDup[i - 1].FileType
                                       + _splitChar.ToString()
                                       + patchDup[i].FileType,
                            LineId = patchDup[i - 1].LineId
                                     + _splitChar.ToString()
                                     + patchDup[i].LineId,
                            JsonPath = patchDup[i - 1].JsonPath
                                       + _splitChar
                                       + patchDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            return report;
        }

        internal static List<ReportItem> EmptyDataViewNames(string methodName)
        {
            var emptyDataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var viewResource in emptyDataViewList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = viewResource.FullFileName,
                    FileType = viewResource.FileType.ToString(),
                    LineId = viewResource.LineId.ToString(),
                    JsonPath = viewResource.JsonPath,
                    Message = $"DataView id \"{viewResource.Value}\" is empty",
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> RedundantDataViews(string methodName)
        {
            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && !n.Shared
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var viewResource in dataViewList)
            {
                if (!_jsonPropertiesCollection
                    .Any(n =>
                        n.ItemType == JsonItemType.Property
                        && n.FileType != JsoncContentType.DataViews
                        && n.Value.Contains(viewResource.Value)))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = viewResource.FullFileName,
                        FileType = viewResource.FileType.ToString(),
                        LineId = viewResource.LineId.ToString(),
                        JsonPath = viewResource.JsonPath,
                        Message = $"DataView \"{viewResource.Value}\" is not used in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> CallNonExistingDataViews(string methodName)
        {
            var dataViewList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    || n.FileType == JsoncContentType.Events
                    && n.Parent == "param"
                    && n.Name == "result"
                    && !string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataViewsList = new List<string>();

                if (property.FileType == JsoncContentType.Events
                    && property.Parent == "trigger"
                    && property.Name == "target"
                    && !string.IsNullOrEmpty(property.Value)
                    && property.Value.Contains('.')
                    && _jsonPropertiesCollection
                        .Any(m =>
                            m.ItemType == JsonItemType.Property
                            && m.FullFileName == property.FullFileName
                            && m.ParentPath == property.ParentPath
                            && m.Name == "type"
                            && m.Value == "EpBinding"))
                {
                    usedDataViewsList = GetTableField(property.Value);
                }
                else if (property.FileType == JsoncContentType.Events
                         && property.Parent == "trigger"
                         && property.Name == "target"
                         && !string.IsNullOrEmpty(property.Value)
                         && !property.Value.Contains('.')
                         && _jsonPropertiesCollection.Any(m =>
                             m.ItemType == JsonItemType.Property
                             && m.FullFileName == property.FullFileName
                             && m.ParentPath == property.ParentPath
                             && m.Name == "type"
                             && m.Value == "DataView"))
                {
                    usedDataViewsList.Add(property.Value + ".");
                }
                else if (property.Name == "epBinding" && !string.IsNullOrEmpty(property.Value))
                {
                    if (property.Value.Contains('.'))
                        usedDataViewsList = GetTableField(property.Value);
                    else
                        usedDataViewsList.Add(property.Value + ".");
                }
                else if (!string.IsNullOrEmpty(property.Value))
                {
                    usedDataViewsList = GetTableField(property.Value);
                }

                foreach (var dataViewItem in usedDataViewsList)
                {
                    if (string.IsNullOrEmpty(dataViewItem))
                        continue;

                    var viewName = dataViewItem.Substring(0, dataViewItem.IndexOf('.'));
                    if (!_systemDataViews.Contains(viewName)
                        && !viewName.StartsWith("%")
                        && !dataViewList.Any(n => n.Value == viewName))
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = property.FullFileName,
                            FileType = property.FileType.ToString(),
                            LineId = property.LineId.ToString(),
                            JsonPath = property.JsonPath,
                            Message = $"DataView \"{viewName}\" is not defined in the project",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            }
            return report;
        }

        internal static List<ReportItem> CallNonExistingDataTables(string methodName)
        {
            var dataTableList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "table"
                    && !string.IsNullOrEmpty(n.Value)).Select(n => n.Value).Distinct();

            var report = new List<ReportItem>();
            foreach (var property in _jsonPropertiesCollection)
            {
                if (property.ItemType != JsonItemType.Property)
                    continue;

                var usedDataTable = "";

                if (property.FileType == JsoncContentType.Events
                         && property.Parent == "trigger"
                         && property.Name == "target"
                         && !string.IsNullOrEmpty(property.Value)
                         && !property.Value.Contains('.')
                         && _jsonPropertiesCollection.Any(m =>
                             m.ItemType == JsonItemType.Property
                             && m.FullFileName == property.FullFileName
                             && m.ParentPath == property.ParentPath
                             && m.Name == "type"
                             && m.Value == "DataTable"))
                {
                    usedDataTable = property.Value;
                }

                if (string.IsNullOrEmpty(usedDataTable))
                    continue;

                if (!usedDataTable.StartsWith("%")
                    && !dataTableList.Contains(usedDataTable))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = property.FullFileName,
                        FileType = property.FileType.ToString(),
                        LineId = property.LineId.ToString(),
                        JsonPath = property.JsonPath,
                        Message = $"DataTable \"{usedDataTable}\" is not defined in the project",
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> OverridingDataViews(string methodName)
        {
            var duplicateViewsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.DataViews
                    && n.Parent == "dataviews"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicateViewsList.Any())
                foreach (var dup in duplicateViewsList)
                {
                    var viewDup = dup.ToArray();
                    for (var i = 1; i < viewDup.Count(); i++)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = viewDup[i - 1].FullFileName
                                           + _splitChar
                                           + viewDup[i].FullFileName,
                            Message = $"DataView \"{viewDup[i - 1].Value}\" is overridden",
                            FileType = viewDup[i - 1].FileType
                                       + _splitChar.ToString()
                                       + viewDup[i].FileType,
                            LineId = viewDup[i - 1].LineId
                                     + _splitChar.ToString()
                                     + viewDup[i].LineId,
                            JsonPath = viewDup[i - 1].JsonPath
                                       + _splitChar
                                       + viewDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            return report;
        }

        internal static List<ReportItem> EmptyRuleNames(string methodName)
        {
            var emptyRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var dup in emptyRulesList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Rule id \"{dup.Value}\" is empty",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> OverridingRules(string methodName)
        {
            var duplicateRulesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Rules
                    && n.Parent == "rules"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicateRulesList.Any())
            {
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
                            Message = $"Rule \"{ruleDup[i - 1].Value}\" is overridden",
                            FileType = ruleDup[i - 1].FileType
                                       + _splitChar.ToString()
                                       + ruleDup[i].FileType,
                            LineId = ruleDup[i - 1].LineId
                                     + _splitChar.ToString()
                                     + ruleDup[i].LineId,
                            JsonPath = ruleDup[i - 1].JsonPath
                                       + _splitChar
                                       + ruleDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            }
            return report;
        }

        internal static List<ReportItem> EmptyToolNames(string methodName)
        {
            var emptyToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && string.IsNullOrEmpty(n.Value));

            var report = new List<ReportItem>();
            foreach (var dup in emptyToolsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Tool id \"{dup.Value}\" is empty",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Error.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> OverridingTools(string methodName)
        {
            var duplicateToolsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Tools
                    && n.Parent == "tools"
                    && n.Name == "id"
                    && !string.IsNullOrEmpty(n.Value))
                .GroupBy(n => n.Value)
                .Where(n => n.Count() > 1);

            var report = new List<ReportItem>();
            if (duplicateToolsList.Any())
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
                            Message = $"Tool \"{toolDup[i - 1].Value}\" is overridden",
                            FileType = toolDup[i - 1].FileType
                                       + _splitChar.ToString()
                                       + toolDup[i].FileType,
                            LineId = toolDup[i - 1].LineId
                                     + _splitChar.ToString()
                                     + toolDup[i].LineId,
                            JsonPath = toolDup[i - 1].JsonPath
                                       + _splitChar
                                       + toolDup[i].JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            return report;
        }

        internal static List<ReportItem> MissingSearches(string methodName)
        {
            var searchesList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "like"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "searchOptions"
                    && _jsonPropertiesCollection.Any(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath == n.ParentPath));

            var report = new List<ReportItem>();
            foreach (var form in searchesList)
            {
                var formSubFolder = _jsonPropertiesCollection
                    .Where(m =>
                        m.ItemType == JsonItemType.Property
                        && m.Name == "searchForm"
                        && !string.IsNullOrEmpty(m.Value)
                        && m.FullFileName == form.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath == form.ParentPath);

                if (!formSubFolder.Any())
                    continue;

                var searchName = form.Value
                    + "\\"
                    + formSubFolder.FirstOrDefault().Value;
                var searchFile = "";
                if (_folderType == FolderType.Unknown)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = "Folder type not recognized (Deployment/Repository/...)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    searchFile = _projectPath
                        + "\\..\\Shared\\search\\"
                        + searchName
                        + "\\search.jsonc";
                }
                else if (_folderType == FolderType.Deployment)
                {
                    searchFile = _projectPath
                        + "\\..\\Shared\\search\\"
                        + searchName
                        + "\\search.jsonc";
                }
                else
                {
                    if (_folderType == FolderType.IceRepository)
                        searchFile = _projectPath
                            + "\\..\\..\\..\\Shared\\search\\"
                            + searchName
                            + "\\search.jsonc";
                    else
                        searchFile = _projectPath
                            + "\\..\\..\\Shared\\search\\"
                            + searchName
                            + "\\search.jsonc";
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
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> MissingForms(string methodName)
        {
            var formsList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "view"
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Parent == "param"
                    && _jsonPropertiesCollection.Any(m =>
                        n.ItemType == JsonItemType.Property
                        && m.Name == "type"
                        && m.Value == "app-open"
                        && m.FullFileName == n.FullFileName
                        && m.FileType == JsoncContentType.Events
                        && m.ParentPath + ".param" == n.ParentPath));

            var report = new List<ReportItem>();
            foreach (var form in formsList)
            {
                List<string> formFileList;
                var fileFound = false;

                if (_folderType == FolderType.Unknown)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = "Folder type not recognized (Deployment/Repository/...)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    formFileList = new List<string>
                    {
                        _projectPath + "\\..\\" + form.Value + "\\events.jsonc"
                    };
                }
                else if (_folderType == FolderType.Deployment)
                {
                    formFileList = new List<string>
                    {
                        _projectPath + "\\..\\" + form.Value + "\\events.jsonc"
                    };
                }
                else if (_folderType == FolderType.IceRepository)
                {
                    formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\..\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\UITrackers\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\..\\ICE\\UITrackers\\" + form.Value + "\\events.jsonc"
                        };
                }
                else
                {
                    formFileList = new List<string>
                        {
                            _projectPath + "\\..\\..\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\UITrackers\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIApps\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIProc\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UIReports\\" + form.Value + "\\events.jsonc",
                            _projectPath + "\\..\\..\\ICE\\UITrackers\\" + form.Value + "\\events.jsonc"
                        };
                }

                foreach (var formFile in formFileList)
                {
                    if (File.Exists(formFile))
                    {
                        fileFound = true;
                        break;
                    }
                }

                if (!fileFound)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = form.FullFileName,
                        Message = $"Call to non-existing form \"{form.Value}\" (Could it be a WinForm?)",
                        FileType = form.FileType.ToString(),
                        LineId = form.LineId.ToString(),
                        JsonPath = form.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Warning.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> JsCode(string methodName)
        {
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.Value));

            var report = new List<ReportItem>();
            foreach (var dup in jsPatternsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"Property value contains JS code \"{dup.Value}\"",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Note.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> JsDataViewCount(string methodName)
        {
            var jsPatternsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && HasJsCode(n.Value)
                    && HasJsDvCount(n.Value));

            var report = new List<ReportItem>();
            foreach (var dup in jsPatternsList)
            {
                var reportItem = new ReportItem
                {
                    ProjectName = _projectName,
                    FullFileName = dup.FullFileName,
                    Message = $"JS code \"{dup.Value}\" must be replaced to \"%DataView.count%\"",
                    FileType = dup.FileType.ToString(),
                    LineId = dup.LineId.ToString(),
                    JsonPath = dup.JsonPath,
                    ValidationType = ValidationTypeEnum.Logic.ToString(),
                    Severity = ImportanceEnum.Warning.ToString(),
                    Source = methodName
                };
                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> MissingLayoutIds(string methodName)
        {
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            var report = new List<ReportItem>();
            foreach (var idItem in layoutIdList)
            {
                var modelId = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == idItem.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.ParentPath == idItem.ParentPath + ".model"
                        && n.Name == "id").ToArray();

                var reportItem = new ReportItem();
                if (!modelId.Any())
                {
                    reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = idItem.FullFileName,
                        Message = $"Layout control id=\"{idItem.Value}\" has no model id",
                        FileType = idItem.FileType.ToString(),
                        LineId = idItem.LineId.ToString(),
                        JsonPath = idItem.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                }
                else
                {
                    continue;
                }

                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> IncorrectLayoutIds(string methodName)
        {
            var layoutIdList = _jsonPropertiesCollection
                .Where(n =>
                    n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.JsonDepth == 2
                    && n.Name == "id");

            var report = new List<ReportItem>();
            foreach (var idItem in layoutIdList)
            {
                var modelId = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == idItem.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.ParentPath == idItem.ParentPath + ".model"
                        && n.Name == "id").ToArray();

                var reportItem = new ReportItem();
                if (!modelId.Any())
                {
                    continue;
                }
                else if (idItem.Value != modelId.First().Value)
                {
                    reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = idItem.FullFileName
                                       + _splitChar
                                       + modelId.First().FullFileName,
                        Message = $"Layout control id=\"{idItem.Value}\" doesn't match model id=\"{modelId.First().Value}\"",
                        FileType = idItem.FileType.ToString()
                                   + _splitChar
                                   + modelId.First().FileType.ToString(),
                        LineId = idItem.LineId.ToString()
                                 + _splitChar
                                 + modelId.First().LineId.ToString(),
                        JsonPath = idItem.JsonPath
                                   + _splitChar
                                   + modelId.First().JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                }
                else
                {
                    continue;
                }

                report.Add(reportItem);
            }
            return report;
        }

        internal static List<ReportItem> IncorrectDVContitionViewName(string methodName)
        {
            var dvConditionsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Name == "type"
                    && n.Value == "dataview-condition");

            var report = new List<ReportItem>();
            foreach (var item in dvConditionsList)
            {
                var dvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "dataview").ToArray();

                var resultDvName = _jsonPropertiesCollection
                    .Where(n =>
                        n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.ParentPath == item.ParentPath + ".param"
                        && n.Name == "result").ToArray();

                if (dvName.Length != 1 || resultDvName.Length != 1)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect dataview-condition definition",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
                if (dvName[0].Value == resultDvName[0].Value)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect dataview-condition definition: \"dataview\" should be different from \"result\"",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = dvName[0].JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                }
            }
            return report;
        }

        internal static List<ReportItem> IncorrectTabIds(string methodName)
        {
            var tabIdsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && !string.IsNullOrEmpty(n.Value)
                    && n.Name == "tabId").Select(n => n.Value).ToArray();

            var tabStripsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Layout
                    && n.Name == "sourceTypeId"
                    && n.Value == "metafx-tabstrip");

            var report = new List<ReportItem>();
            foreach (var tab in tabStripsList)
            {
                var tabsList = _jsonPropertiesCollection
                    .Where(n =>
                        !n.Shared
                        && n.FullFileName == tab.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Layout
                        && n.JsonPath.Contains(tab.ParentPath + ".model.data")
                        && n.Name == "page"
                        && !string.IsNullOrEmpty(n.Value));

                foreach (var item in tabsList)
                {
                    if (!tabIdsList.Contains(item.Value))
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"Inexistent tab link: {item.Value}",
                            FileType = item.FileType.ToString(),
                            LineId = item.LineId.ToString(),
                            JsonPath = item.JsonPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }
            }
            return report;
        }

        internal static List<ReportItem> IncorrectRestCalls(string methodName)
        {
            var restCallsList = _jsonPropertiesCollection
                .Where(n =>
                    !n.Shared
                    && n.ItemType == JsonItemType.Property
                    && n.FileType == JsoncContentType.Events
                    && n.Value == "rest-erp"
                    && n.Name == "type");

            var report = new List<ReportItem>();
            foreach (var item in restCallsList)
            {
                var serviceName = _jsonPropertiesCollection
                    .Where(n =>
                        !n.Shared
                        && n.FullFileName == item.FullFileName
                        && n.ItemType == JsonItemType.Property
                        && n.FileType == JsoncContentType.Events
                        && n.JsonPath == item.ParentPath + ".param.svc"
                        && !string.IsNullOrEmpty(n.Value)).FirstOrDefault();

                var svcMethodName = _jsonPropertiesCollection
                   .Where(n =>
                       !n.Shared
                       && n.FullFileName == item.FullFileName
                       && n.ItemType == JsonItemType.Property
                       && n.FileType == JsoncContentType.Events
                       && n.JsonPath == item.ParentPath + ".param.svcPath"
                       && !string.IsNullOrEmpty(n.Value)).FirstOrDefault();

                var methodParamsList = _jsonPropertiesCollection
                   .Where(n =>
                       !n.Shared
                       && n.FullFileName == item.FullFileName
                       && n.ItemType == JsonItemType.Property
                       && n.FileType == JsoncContentType.Events
                       && ((!GetParentName(n.ParentPath).StartsWith("params[")
                       && n.JsonPath.StartsWith(item.ParentPath + ".param.methodParameters")
                       && n.Name == "field")
                       || (n.JsonPath.StartsWith(item.ParentPath + ".param.erpRestPostArgs")
                       && n.Name == "paramPath")));

                if (serviceName == null || string.IsNullOrEmpty(serviceName.Value))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"REST service name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(serviceName.Value) || IsTableField(serviceName.Value) || HasJsCode(serviceName.Value))
                {
                    continue;
                }

                var serverParams = GetRestCallParams(serviceName.Value, svcMethodName.Value, _serverAssembliesPath);

                if (string.IsNullOrEmpty(serverParams.svcName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect REST service name: {serviceName.Value}",
                        FileType = serviceName.FileType.ToString(),
                        LineId = serviceName.LineId.ToString(),
                        JsonPath = serviceName.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (IsPatch(svcMethodName.Value) || IsTableField(svcMethodName.Value) || HasJsCode(svcMethodName.Value))
                {
                    continue;
                }

                if (svcMethodName == null || string.IsNullOrEmpty(svcMethodName?.Value))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"REST method name not defined",
                        FileType = item.FileType.ToString(),
                        LineId = item.LineId.ToString(),
                        JsonPath = item.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                if (string.IsNullOrEmpty(serverParams.methodName))
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Incorrect REST service '{serviceName.Value}' method name: {svcMethodName.Value}",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.JsonPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);
                    continue;
                }

                // if failed to get params (no Epicor.ServceModel.dll found)
                if (serverParams.parameters == null)
                {
                    var reportItem = new ReportItem
                    {
                        ProjectName = _projectName,
                        FullFileName = item.FullFileName,
                        Message = $"Failed to retrieve REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameters (Epicor.ServiceMode.dll not found?)",
                        FileType = svcMethodName.FileType.ToString(),
                        LineId = svcMethodName.LineId.ToString(),
                        JsonPath = svcMethodName.ParentPath,
                        ValidationType = ValidationTypeEnum.Logic.ToString(),
                        Severity = ImportanceEnum.Error.ToString(),
                        Source = methodName
                    };
                    report.Add(reportItem);

                    continue;
                }

                var missingParams = new StringBuilder();
                if (methodParamsList?.Count() > 0)
                {
                    foreach (var par in methodParamsList)
                    {
                        if (IsPatch(par.Value) || IsTableField(par.Value) || HasJsCode(par.Value))
                            continue;

                        if (!serverParams.parameters.Contains(par.Value))
                        {
                            missingParams.Append(par.Value + ", ");
                        }
                    }

                    if (missingParams.Length > 0)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"Incorrect REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameter names: {missingParams}",
                            FileType = methodParamsList.FirstOrDefault()?.FileType.ToString(),
                            LineId = methodParamsList.FirstOrDefault()?.LineId.ToString(),
                            JsonPath = methodParamsList.FirstOrDefault()?.ParentPath,
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }

                missingParams = new StringBuilder();
                if (serverParams.parameters?.Count > 0)
                {
                    var methodParamsTmp = methodParamsList.Select(t => t.Value);
                    foreach (var par in serverParams.parameters.Where(t => t != "ds"))
                    {
                        if (!methodParamsTmp.Contains(par))
                        {
                            missingParams.Append(par + ", ");
                        }
                    }

                    if (missingParams.Length > 0)
                    {
                        var reportItem = new ReportItem
                        {
                            ProjectName = _projectName,
                            FullFileName = item.FullFileName,
                            Message = $"Missing REST service '{serviceName.Value}' method '{svcMethodName.Value}' parameter names: {missingParams}",
                            FileType = svcMethodName.FileType.ToString(),
                            LineId = svcMethodName.LineId.ToString(),
                            JsonPath = svcMethodName.ParentPath + ".methodParameters",
                            ValidationType = ValidationTypeEnum.Logic.ToString(),
                            Severity = ImportanceEnum.Error.ToString(),
                            Source = methodName
                        };
                        report.Add(reportItem);
                    }
                }

            }
            return report;
        }
        #endregion
        // misprints in property/dataview/string/id names (try finding lower-case property in schema or project scope)
        // searches must use only one dataview
    }
}
