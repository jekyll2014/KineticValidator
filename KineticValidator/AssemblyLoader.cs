using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace KineticValidator
{
    public class AssemblyLoader : MarshalByRefObject
    {
        private Assembly assembly = null;

        public bool LoadAssembly(string svcName, string assemblyPath)
        {
            var fileName = GetAssemblyContractName(svcName);

            if (string.IsNullOrEmpty(fileName))
            {
                return false;
            }

            try
            {
                assembly = Assembly.LoadFile(assemblyPath + "\\" + fileName);
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
                foreach (var lException in ex.LoaderExceptions)
                {
                    Utilities.SaveDevLog(lException.Message + Environment.NewLine);
                }
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
                Utilities.SaveDevLog(ex.Message);
            }

            return methodsList;
        }

        public List<string> GetParamsSafely(string svcName, string methodName, string assemblyPath)
        {
            var contractName = GetAssemblyContractName(svcName);
            if (string.IsNullOrEmpty(contractName))
            {
                return null;
            }

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
                foreach (var lException in ex.LoaderExceptions)
                {
                    Utilities.SaveDevLog(lException.Message + Environment.NewLine);
                }
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
                    catch (Exception ex)
                    {
                        Utilities.SaveDevLog(ex.Message);
                    }

                    break;
                }
            }

            return paramList;
        }
    }
}
