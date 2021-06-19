// This is an independent project of an individual developer. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++, C#, and Java: http://www.viva64.com

using System.Windows.Forms;
using ScintillaNET;

namespace JsonEditorForm
{
    internal class SearchManager
    {
        public static Scintilla TextArea;
        public static TextBox SearchBox;

        public static string LastSearch = "";

        private static int _lastSearchIndex;

        public static void Find(bool next, bool incremental)
        {
            //var first = LastSearch != SearchBox.FormCaption;

            LastSearch = SearchBox.Text;
            if (LastSearch.Length > 0)
            {
                if (next)
                {
                    // SEARCH FOR THE NEXT OCCURANCE

                    // Search the document at the last search index
                    TextArea.TargetStart = _lastSearchIndex - 1;
                    TextArea.TargetEnd = _lastSearchIndex + LastSearch.Length + 1;
                    TextArea.SearchFlags = SearchFlags.None;

                    // Search, and if not found..
                    if (!incremental || TextArea.SearchInTarget(LastSearch) == -1)
                    {
                        // Search the document from the caret onwards
                        TextArea.TargetStart = TextArea.CurrentPosition;
                        TextArea.TargetEnd = TextArea.TextLength;
                        TextArea.SearchFlags = SearchFlags.None;

                        // Search, and if not found..
                        if (TextArea.SearchInTarget(LastSearch) == -1)
                        {
                            // Search again from top
                            TextArea.TargetStart = 0;
                            TextArea.TargetEnd = TextArea.TextLength;

                            // Search, and if not found..
                            if (TextArea.SearchInTarget(LastSearch) == -1)
                            {
                                // clear selection and exit
                                TextArea.ClearSelections();
                                return;
                            }
                        }
                    }
                }
                else
                {
                    // SEARCH FOR THE PREVIOUS OCCURANCE

                    // Search the document from the beginning to the caret
                    TextArea.TargetStart = 0;
                    TextArea.TargetEnd = TextArea.CurrentPosition;
                    TextArea.SearchFlags = SearchFlags.None;

                    // Search, and if not found..
                    if (TextArea.SearchInTarget(LastSearch) == -1)
                    {
                        // Search again from the caret onwards
                        TextArea.TargetStart = TextArea.CurrentPosition;
                        TextArea.TargetEnd = TextArea.TextLength;

                        // Search, and if not found..
                        if (TextArea.SearchInTarget(LastSearch) == -1)
                        {
                            // clear selection and exit
                            TextArea.ClearSelections();
                            return;
                        }
                    }
                }

                // Select the occurance
                _lastSearchIndex = TextArea.TargetStart;
                TextArea.SetSelection(TextArea.TargetEnd, TextArea.TargetStart);
                TextArea.ScrollCaret();
            }

            SearchBox.Focus();
        }
    }
}
