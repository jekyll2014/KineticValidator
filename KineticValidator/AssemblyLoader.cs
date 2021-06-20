// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace KineticValidator
{
    public class AssemblyLoader : MarshalByRefObject
    {
        private Assembly _assembly;
        private Type _typeSafely;
        private bool _assemblyLoadRestrictions;
        private Dictionary<string, string[]> _allMethodsInfo; // method / paremeters
        private Dictionary<string, Dictionary<string, string[]>> _allDataSetsInfo;
        public bool IsLoaded => !(_assembly == null);

        public Dictionary<string, string[]> AllMethodsInfo => _allMethodsInfo ?? (_allMethodsInfo = GetMethodTree());

        public Dictionary<string, Dictionary<string, string[]>> AllDataSetsInfo =>
            _allDataSetsInfo ?? (_allDataSetsInfo = GetDataSetTree());

        public bool LoadAssembly(string svcName, string assemblyPath)
        {
            _allMethodsInfo = null;
            _allDataSetsInfo = null;
            var fileName = GetAssemblyContractName(svcName);

            if (string.IsNullOrEmpty(fileName)) return false;

            var assemblyFileName = assemblyPath + "\\" + fileName;
            try
            {
                _assembly = Assembly.LoadFile(assemblyFileName);
            }
            catch (Exception)
            {
            }

            return true;
        }

        private string GetAssemblyContractName(string svcName)
        {
            var fileName = "";
            var nameTokens = svcName.ToUpper().Split('.');
            if (nameTokens.Length != 3) return fileName;

            // 1st token must be ["ERP","ICE"]
            if (!new[] { "ERP", "ICE" }.Contains(nameTokens[0])) return fileName;

            // 2nd token must be ["BO,"LIB","PROC","RPT","SEC","WEB"]
            if (!new[] { "BO", "LIB", "PROC", "RPT", "SEC", "WEB" }.Contains(nameTokens[1])) return fileName;

            nameTokens[2] = nameTokens[2].Replace("SVC", "");
            fileName = nameTokens[0] + ".Contracts." + nameTokens[1] + "." + nameTokens[2] + ".dll";

            return fileName;
        }

        private Dictionary<string, string[]> GetMethodTree()
        {
            var methods = GetMethodsSafely();
            var methodTree = new Dictionary<string, string[]>();
            if (methods != null)
            {
                foreach (var method in methods)
                {
                    var parameters = GetParamsSafely(method);
                    methodTree.Add(method, parameters);
                }
            }

            return methodTree;
        }

        private string[] GetMethodsSafely()
        {
            if (_assembly == null) return null;

            try
            {
                _typeSafely = _assembly.GetTypes().FirstOrDefault(t => t.Name.EndsWith("SvcContract") && t.IsPublic);
            }
            catch (ReflectionTypeLoadException ex)
            {
                _assemblyLoadRestrictions = true;
                _typeSafely = ex.Types.FirstOrDefault(t => t.Name.EndsWith("SvcContract") && t.IsPublic);
                foreach (var lException in ex.LoaderExceptions)
                    Utilities.SaveDevLog(lException.Message + Environment.NewLine);
            }

            if (_typeSafely == null) return null;

            string[] methods;
            try
            {
                methods = _typeSafely.GetMethods().Select(t => t.Name).ToArray();
            }
            catch (Exception ex)
            {
                methods = null;
                Utilities.SaveDevLog(ex.Message);
            }

            return methods;
        }

        private string[] GetParamsSafely(string methodName)
        {
            if (_assembly == null || _typeSafely == null || _assemblyLoadRestrictions) return null;

            string[] paramList = null;

            var method = _typeSafely.GetMethod(methodName);
            if (method != null)
                try
                {
                    var parameters = method.GetParameters();
                    paramList = parameters.Where(t => !t.IsOut).Select(t => t.Name).ToArray();
                }
                catch (Exception ex)
                {
                    Utilities.SaveDevLog(ex.Message);
                }

            return paramList;
        }

        public string[] GetMethods()
        {
            if (_allMethodsInfo == null) _allMethodsInfo = GetMethodTree();

            return _allMethodsInfo.Select(n => n.Key).ToArray();
        }

        public string[] GetParameters(string methodName)
        {
            if (_allMethodsInfo == null) _allMethodsInfo = GetMethodTree();

            _allMethodsInfo.TryGetValue(methodName, out var methodTree);
            return methodTree?.ToArray();
        }

        private Dictionary<string, Dictionary<string, string[]>> GetDataSetTree()
        {
            if (_typeSafely == null || _assemblyLoadRestrictions) return null;

            var fullEntityList = _typeSafely.Assembly.DefinedTypes.ToArray();
            var dataSets = fullEntityList.Where(n => n.Name.EndsWith("DataSet") && !n.Name.StartsWith("UpdExt"))
                .ToArray();

            var dataSetTree = new Dictionary<string, Dictionary<string, string[]>>();
            foreach (var dataSet in dataSets)
            {
                var dataSetName = dataSet.Name.Replace("DataSet", "");
                var tableNames = dataSet.DeclaredFields.Where(n => !n.Name.StartsWith("_"))
                    .Select(n => n.Name.Replace("table", "")).ToArray();
                var dataTableTree = new Dictionary<string, string[]>();
                foreach (var tableName in tableNames)
                {
                    var fieldNames = fullEntityList
                        .FirstOrDefault(n =>
                            n.Name == tableName + "Row" && !string.IsNullOrEmpty(n.FullName) &&
                            !n.FullName.Contains('+'))?.DeclaredFields.Select(n => n.Name.TrimStart('_')).ToList();
                    fieldNames?.Remove("SpecifiedProperties");
                    fieldNames?.Remove("ColumnNames");
                    dataTableTree.Add(tableName, fieldNames?.ToArray());
                }

                dataSetTree.Add(dataSetName, dataTableTree);
            }

            return dataSetTree;
        }

        public string[] GetDataSets()
        {
            if (_allDataSetsInfo == null) _allDataSetsInfo = GetDataSetTree();

            return _allDataSetsInfo?.Select(n => n.Key).ToArray();
        }

        public string[] GetTables(string dataSetName)
        {
            if (_allDataSetsInfo == null) _allDataSetsInfo = GetDataSetTree();

            if (_allDataSetsInfo == null) return null;

            _allDataSetsInfo.TryGetValue(dataSetName, out var tableTree);
            return tableTree?.Select(n => n.Key).ToArray();
        }

        public string[] GetFields(string dataSetName, string tableName)
        {
            if (_allDataSetsInfo == null) _allDataSetsInfo = GetDataSetTree();

            if (_allDataSetsInfo == null) return null;

            _allDataSetsInfo.TryGetValue(dataSetName, out var tableTree);
            string[] fields = null;
            tableTree?.TryGetValue(tableName, out fields);
            return fields;
        }
    }
}
