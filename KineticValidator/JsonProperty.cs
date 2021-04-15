using System;
using System.Runtime.Serialization;

namespace KineticValidator
{
    [DataContract]
    [Serializable]
    public enum JsoncContentType
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
        [DataMember] public JsoncContentType FileType; // file type (event, string, rules, ...)
        [DataMember] public string Version; // schema version declared in the beginning of the file

        [DataMember]
        public JsonItemType ItemType; // type of the property as per JSON classification (property, array, object)

        [DataMember] public string Parent; // parent name
        [DataMember] public bool Shared; // is original file in shared or project folder
        [DataMember] public int StartPosition; // property beginning byte # in the original file
        [DataMember] public int EndPosition; // property ending byte # in the original file

        public string ParentPath // parent object path
        {
            get
            {
                var parentPath = "";
                if (!string.IsNullOrEmpty(JsonPath) && JsonPath.Contains("."))
                    parentPath = JsonPath.Substring(0, JsonPath.LastIndexOf('.'));
                return parentPath;
            }
        }

        public JsonProperty()
        {
            FullFileName = "";
            JsonPath = "";
            JsonDepth = 0;
            Name = "";
            Value = "";
            FileType = JsoncContentType.Unknown;
            Version = "";
            ItemType = JsonItemType.Unknown;
            Parent = "";
            Shared = false;
            StartPosition = -1;
            EndPosition = -1;
        }
    }
}
