// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System;
using System.Runtime.Serialization;

namespace KineticValidator
{
    [DataContract]
    [Serializable]
    public enum KineticContentType
    {
        [EnumMember] Unknown,
        [EnumMember] DataViews,
        [EnumMember] Events,
        [EnumMember] Layout,
        [EnumMember] Rules,
        [EnumMember] Search,
        [EnumMember] Combo,
        [EnumMember] Tools,
        [EnumMember] Strings,
        [EnumMember] Patch
    }

    public enum JsonItemType
    {
        [EnumMember] Unknown,
        [EnumMember] Property,
        [EnumMember] Object,
        [EnumMember] Array
    }

    public class JsonProperty
    {
        [DataMember] public int LineId; // line # in complete project properties collection
        [DataMember] public string FullFileName; // original path + file name
        [DataMember] public string JsonPath; // JSON path of the property
        [DataMember] public int JsonDepth; // depth in the original JSON structure
        [DataMember] public string Name; // property name
        [DataMember] public string Value; // property value
        [DataMember] public KineticContentType FileType; // file type (event, string, rules, ...)
        [DataMember] public string Version; // schema version declared in the beginning of the file

        [DataMember]
        public JsonItemType ItemType; // type of the property as per JSON classification (property, array, object)

        [DataMember] public string Parent; // parent name
        [DataMember] public bool Shared; // is original file in shared or project folder
        [DataMember] public int SourceLineNumber; // line number in the source file
        [DataMember] public int StartPosition; // property beginning byte # in the original file
        [DataMember] public int EndPosition; // property ending byte # in the original file

        [DataMember] private string _patchedValue; // property value
        public string PatchedValue // parent object path
        {
            get
            {
                return _patchedValue ?? Value;
            }
            set => _patchedValue = value;
        }


        [DataMember] private string _parentPath; // property value
        public string ParentPath // parent object path
        {
            get
            {
                if (_parentPath == null)
                {
                    if (!string.IsNullOrEmpty(JsonPath) && JsonPath.Contains("."))
                        _parentPath = JsonPath.Substring(0, JsonPath.LastIndexOf('.'));
                    else
                        _parentPath = "";
                }

                return _parentPath;
            }
        }

        public JsonProperty()
        {
            FullFileName = "";
            JsonPath = "";
            JsonDepth = 0;
            Name = "";
            Value = "";
            FileType = KineticContentType.Unknown;
            Version = "";
            ItemType = JsonItemType.Unknown;
            Parent = "";
            Shared = false;
            SourceLineNumber = -1;
            StartPosition = -1;
            EndPosition = -1;
        }
    }
}
