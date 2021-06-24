
namespace KineticValidator
{
    partial class MainForm
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            System.Windows.Forms.DataGridViewCellStyle dataGridViewCellStyle1 = new System.Windows.Forms.DataGridViewCellStyle();
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage_DataCollection = new System.Windows.Forms.TabPage();
            this.splitContainer1 = new System.Windows.Forms.SplitContainer();
            this.checkBox_vsCode = new System.Windows.Forms.CheckBox();
            this.checkBox_saveReport = new System.Windows.Forms.CheckBox();
            this.checkBox_saveFiles = new System.Windows.Forms.CheckBox();
            this.button_validateAll = new System.Windows.Forms.Button();
            this.checkBox_showPreview = new System.Windows.Forms.CheckBox();
            this.checkedListBox_validators = new System.Windows.Forms.CheckedListBox();
            this.checkBox_reformatJson = new System.Windows.Forms.CheckBox();
            this.checkBox_skipSchemaProblems = new System.Windows.Forms.CheckBox();
            this.checkBox_alwaysOnTop = new System.Windows.Forms.CheckBox();
            this.checkBox_ignoreHttpsError = new System.Windows.Forms.CheckBox();
            this.button_validateProject = new System.Windows.Forms.Button();
            this.button_selectAssemblyFolder = new System.Windows.Forms.Button();
            this.button_SelectProject = new System.Windows.Forms.Button();
            this.textBox_logText = new System.Windows.Forms.TextBox();
            this.tabPage_Report = new System.Windows.Forms.TabPage();
            this.dataGridView_report = new System.Windows.Forms.DataGridView();
            this.contextMenuStrip1 = new System.Windows.Forms.ContextMenuStrip(this.components);
            this.removeThisErrorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.alwaysIgnoreThisErrorToolStripMenuItem = new System.Windows.Forms.ToolStripMenuItem();
            this.folderBrowserDialog1 = new System.Windows.Forms.FolderBrowserDialog();
            this.checkBox_applyPatches = new System.Windows.Forms.CheckBox();
            this.tabControl1.SuspendLayout();
            this.tabPage_DataCollection.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).BeginInit();
            this.splitContainer1.Panel1.SuspendLayout();
            this.splitContainer1.Panel2.SuspendLayout();
            this.splitContainer1.SuspendLayout();
            this.tabPage_Report.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_report)).BeginInit();
            this.contextMenuStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage_DataCollection);
            this.tabControl1.Controls.Add(this.tabPage_Report);
            this.tabControl1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(800, 450);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPage_DataCollection
            // 
            this.tabPage_DataCollection.Controls.Add(this.splitContainer1);
            this.tabPage_DataCollection.Location = new System.Drawing.Point(4, 22);
            this.tabPage_DataCollection.Name = "tabPage_DataCollection";
            this.tabPage_DataCollection.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_DataCollection.Size = new System.Drawing.Size(792, 424);
            this.tabPage_DataCollection.TabIndex = 0;
            this.tabPage_DataCollection.Text = "Data collection";
            this.tabPage_DataCollection.UseVisualStyleBackColor = true;
            // 
            // splitContainer1
            // 
            this.splitContainer1.Dock = System.Windows.Forms.DockStyle.Fill;
            this.splitContainer1.Location = new System.Drawing.Point(3, 3);
            this.splitContainer1.Name = "splitContainer1";
            // 
            // splitContainer1.Panel1
            // 
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_applyPatches);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_vsCode);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_saveReport);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_saveFiles);
            this.splitContainer1.Panel1.Controls.Add(this.button_validateAll);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_showPreview);
            this.splitContainer1.Panel1.Controls.Add(this.checkedListBox_validators);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_reformatJson);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_skipSchemaProblems);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_alwaysOnTop);
            this.splitContainer1.Panel1.Controls.Add(this.checkBox_ignoreHttpsError);
            this.splitContainer1.Panel1.Controls.Add(this.button_validateProject);
            this.splitContainer1.Panel1.Controls.Add(this.button_selectAssemblyFolder);
            this.splitContainer1.Panel1.Controls.Add(this.button_SelectProject);
            // 
            // splitContainer1.Panel2
            // 
            this.splitContainer1.Panel2.Controls.Add(this.textBox_logText);
            this.splitContainer1.Size = new System.Drawing.Size(786, 418);
            this.splitContainer1.SplitterDistance = 244;
            this.splitContainer1.TabIndex = 1;
            // 
            // checkBox_vsCode
            // 
            this.checkBox_vsCode.AutoSize = true;
            this.checkBox_vsCode.Location = new System.Drawing.Point(1, 189);
            this.checkBox_vsCode.Name = "checkBox_vsCode";
            this.checkBox_vsCode.Size = new System.Drawing.Size(87, 17);
            this.checkBox_vsCode.TabIndex = 7;
            this.checkBox_vsCode.Text = "Use VSCode";
            this.checkBox_vsCode.UseVisualStyleBackColor = true;
            this.checkBox_vsCode.CheckedChanged += new System.EventHandler(this.CheckBox_vsCode_CheckedChanged);
            // 
            // checkBox_saveReport
            // 
            this.checkBox_saveReport.AutoSize = true;
            this.checkBox_saveReport.Location = new System.Drawing.Point(1, 235);
            this.checkBox_saveReport.Name = "checkBox_saveReport";
            this.checkBox_saveReport.Size = new System.Drawing.Size(112, 17);
            this.checkBox_saveReport.TabIndex = 6;
            this.checkBox_saveReport.Text = "Save JSON report";
            this.checkBox_saveReport.UseVisualStyleBackColor = true;
            this.checkBox_saveReport.CheckedChanged += new System.EventHandler(this.CheckBox_saveReport_CheckedChanged);
            // 
            // checkBox_saveFiles
            // 
            this.checkBox_saveFiles.AutoSize = true;
            this.checkBox_saveFiles.Location = new System.Drawing.Point(1, 258);
            this.checkBox_saveFiles.Name = "checkBox_saveFiles";
            this.checkBox_saveFiles.Size = new System.Drawing.Size(96, 17);
            this.checkBox_saveFiles.TabIndex = 5;
            this.checkBox_saveFiles.Text = "Save data files";
            this.checkBox_saveFiles.UseVisualStyleBackColor = true;
            this.checkBox_saveFiles.CheckedChanged += new System.EventHandler(this.CheckBox_saveFiles_CheckedChanged);
            // 
            // button_validateAll
            // 
            this.button_validateAll.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.button_validateAll.Location = new System.Drawing.Point(1, 91);
            this.button_validateAll.Name = "button_validateAll";
            this.button_validateAll.Size = new System.Drawing.Size(241, 23);
            this.button_validateAll.TabIndex = 4;
            this.button_validateAll.Text = "Validate projects in folder";
            this.button_validateAll.UseVisualStyleBackColor = true;
            this.button_validateAll.Click += new System.EventHandler(this.Button_validateAll_Click);
            // 
            // checkBox_showPreview
            // 
            this.checkBox_showPreview.AutoSize = true;
            this.checkBox_showPreview.Location = new System.Drawing.Point(1, 166);
            this.checkBox_showPreview.Name = "checkBox_showPreview";
            this.checkBox_showPreview.Size = new System.Drawing.Size(93, 17);
            this.checkBox_showPreview.TabIndex = 3;
            this.checkBox_showPreview.Text = "Show preview";
            this.checkBox_showPreview.UseVisualStyleBackColor = true;
            this.checkBox_showPreview.CheckedChanged += new System.EventHandler(this.CheckBox_showPreview_CheckedChanged);
            // 
            // checkedListBox_validators
            // 
            this.checkedListBox_validators.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom) 
            | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.checkedListBox_validators.CheckOnClick = true;
            this.checkedListBox_validators.FormattingEnabled = true;
            this.checkedListBox_validators.Location = new System.Drawing.Point(-3, 327);
            this.checkedListBox_validators.Name = "checkedListBox_validators";
            this.checkedListBox_validators.Size = new System.Drawing.Size(245, 94);
            this.checkedListBox_validators.TabIndex = 2;
            this.checkedListBox_validators.ItemCheck += new System.Windows.Forms.ItemCheckEventHandler(this.CheckedListBox_validators_ItemCheck);
            this.checkedListBox_validators.Leave += new System.EventHandler(this.CheckedListBox_validators_Leave);
            // 
            // checkBox_reformatJson
            // 
            this.checkBox_reformatJson.AutoSize = true;
            this.checkBox_reformatJson.Location = new System.Drawing.Point(1, 212);
            this.checkBox_reformatJson.Name = "checkBox_reformatJson";
            this.checkBox_reformatJson.Size = new System.Drawing.Size(100, 17);
            this.checkBox_reformatJson.TabIndex = 1;
            this.checkBox_reformatJson.Text = "Reformat JSON";
            this.checkBox_reformatJson.UseVisualStyleBackColor = true;
            this.checkBox_reformatJson.CheckedChanged += new System.EventHandler(this.CheckBox_reformatJson_CheckedChanged);
            // 
            // checkBox_skipSchemaProblems
            // 
            this.checkBox_skipSchemaProblems.AutoSize = true;
            this.checkBox_skipSchemaProblems.Location = new System.Drawing.Point(1, 143);
            this.checkBox_skipSchemaProblems.Name = "checkBox_skipSchemaProblems";
            this.checkBox_skipSchemaProblems.Size = new System.Drawing.Size(162, 17);
            this.checkBox_skipSchemaProblems.TabIndex = 1;
            this.checkBox_skipSchemaProblems.Text = "Skip annoying schema errors";
            this.checkBox_skipSchemaProblems.UseVisualStyleBackColor = true;
            this.checkBox_skipSchemaProblems.CheckedChanged += new System.EventHandler(this.CheckBox_skipSchemaProblems_CheckedChanged);
            // 
            // checkBox_alwaysOnTop
            // 
            this.checkBox_alwaysOnTop.AutoSize = true;
            this.checkBox_alwaysOnTop.Location = new System.Drawing.Point(1, 281);
            this.checkBox_alwaysOnTop.Name = "checkBox_alwaysOnTop";
            this.checkBox_alwaysOnTop.Size = new System.Drawing.Size(92, 17);
            this.checkBox_alwaysOnTop.TabIndex = 1;
            this.checkBox_alwaysOnTop.Text = "Always on top";
            this.checkBox_alwaysOnTop.UseVisualStyleBackColor = true;
            this.checkBox_alwaysOnTop.CheckedChanged += new System.EventHandler(this.CheckBox_alwaysOnTop_CheckedChanged);
            // 
            // checkBox_ignoreHttpsError
            // 
            this.checkBox_ignoreHttpsError.AutoSize = true;
            this.checkBox_ignoreHttpsError.Location = new System.Drawing.Point(1, 120);
            this.checkBox_ignoreHttpsError.Name = "checkBox_ignoreHttpsError";
            this.checkBox_ignoreHttpsError.Size = new System.Drawing.Size(119, 17);
            this.checkBox_ignoreHttpsError.TabIndex = 1;
            this.checkBox_ignoreHttpsError.Text = "Ignore HTTPS error";
            this.checkBox_ignoreHttpsError.UseVisualStyleBackColor = true;
            this.checkBox_ignoreHttpsError.CheckedChanged += new System.EventHandler(this.CheckBox_ignoreHttpsError_CheckedChanged);
            // 
            // button_validateProject
            // 
            this.button_validateProject.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.button_validateProject.Enabled = false;
            this.button_validateProject.Location = new System.Drawing.Point(1, 61);
            this.button_validateProject.Name = "button_validateProject";
            this.button_validateProject.Size = new System.Drawing.Size(241, 23);
            this.button_validateProject.TabIndex = 0;
            this.button_validateProject.Text = "Validate project";
            this.button_validateProject.UseVisualStyleBackColor = true;
            this.button_validateProject.Click += new System.EventHandler(this.Button_validateProject_Click);
            // 
            // button_selectAssemblyFolder
            // 
            this.button_selectAssemblyFolder.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.button_selectAssemblyFolder.Location = new System.Drawing.Point(1, 3);
            this.button_selectAssemblyFolder.Name = "button_selectAssemblyFolder";
            this.button_selectAssemblyFolder.Size = new System.Drawing.Size(241, 23);
            this.button_selectAssemblyFolder.TabIndex = 0;
            this.button_selectAssemblyFolder.Text = "Select assembly folder";
            this.button_selectAssemblyFolder.UseVisualStyleBackColor = true;
            this.button_selectAssemblyFolder.Click += new System.EventHandler(this.Button_selectAssemblyFolder_Click);
            // 
            // button_SelectProject
            // 
            this.button_SelectProject.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left) 
            | System.Windows.Forms.AnchorStyles.Right)));
            this.button_SelectProject.Location = new System.Drawing.Point(1, 32);
            this.button_SelectProject.Name = "button_SelectProject";
            this.button_SelectProject.Size = new System.Drawing.Size(241, 23);
            this.button_SelectProject.TabIndex = 0;
            this.button_SelectProject.Text = "Select project folder";
            this.button_SelectProject.UseVisualStyleBackColor = true;
            this.button_SelectProject.Click += new System.EventHandler(this.Button_selectFiles_Click);
            // 
            // textBox_logText
            // 
            this.textBox_logText.Dock = System.Windows.Forms.DockStyle.Fill;
            this.textBox_logText.Location = new System.Drawing.Point(0, 0);
            this.textBox_logText.Multiline = true;
            this.textBox_logText.Name = "textBox_logText";
            this.textBox_logText.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.textBox_logText.Size = new System.Drawing.Size(538, 418);
            this.textBox_logText.TabIndex = 0;
            // 
            // tabPage_Report
            // 
            this.tabPage_Report.Controls.Add(this.dataGridView_report);
            this.tabPage_Report.Location = new System.Drawing.Point(4, 22);
            this.tabPage_Report.Name = "tabPage_Report";
            this.tabPage_Report.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage_Report.Size = new System.Drawing.Size(792, 424);
            this.tabPage_Report.TabIndex = 1;
            this.tabPage_Report.Text = "Report";
            this.tabPage_Report.UseVisualStyleBackColor = true;
            // 
            // dataGridView_report
            // 
            this.dataGridView_report.AllowUserToAddRows = false;
            this.dataGridView_report.AllowUserToDeleteRows = false;
            this.dataGridView_report.AllowUserToOrderColumns = true;
            this.dataGridView_report.ColumnHeadersHeightSizeMode = System.Windows.Forms.DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            this.dataGridView_report.ContextMenuStrip = this.contextMenuStrip1;
            dataGridViewCellStyle1.Alignment = System.Windows.Forms.DataGridViewContentAlignment.MiddleLeft;
            dataGridViewCellStyle1.BackColor = System.Drawing.SystemColors.Window;
            dataGridViewCellStyle1.Font = new System.Drawing.Font("Microsoft Sans Serif", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
            dataGridViewCellStyle1.ForeColor = System.Drawing.SystemColors.ControlText;
            dataGridViewCellStyle1.SelectionBackColor = System.Drawing.SystemColors.Highlight;
            dataGridViewCellStyle1.SelectionForeColor = System.Drawing.SystemColors.HighlightText;
            dataGridViewCellStyle1.WrapMode = System.Windows.Forms.DataGridViewTriState.True;
            this.dataGridView_report.DefaultCellStyle = dataGridViewCellStyle1;
            this.dataGridView_report.Dock = System.Windows.Forms.DockStyle.Fill;
            this.dataGridView_report.Location = new System.Drawing.Point(3, 3);
            this.dataGridView_report.Name = "dataGridView_report";
            this.dataGridView_report.Size = new System.Drawing.Size(786, 418);
            this.dataGridView_report.TabIndex = 0;
            this.dataGridView_report.CellDoubleClick += new System.Windows.Forms.DataGridViewCellEventHandler(this.DataGridView_report_CellDoubleClick);
            this.dataGridView_report.CellMouseDown += new System.Windows.Forms.DataGridViewCellMouseEventHandler(this.DataGridView_report_CellMouseDown);
            this.dataGridView_report.KeyDown += new System.Windows.Forms.KeyEventHandler(this.DataGridView_report_KeyDown);
            // 
            // contextMenuStrip1
            // 
            this.contextMenuStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.removeThisErrorToolStripMenuItem,
            this.alwaysIgnoreThisErrorToolStripMenuItem});
            this.contextMenuStrip1.Name = "contextMenuStrip1";
            this.contextMenuStrip1.Size = new System.Drawing.Size(199, 48);
            // 
            // removeThisErrorToolStripMenuItem
            // 
            this.removeThisErrorToolStripMenuItem.Name = "removeThisErrorToolStripMenuItem";
            this.removeThisErrorToolStripMenuItem.Size = new System.Drawing.Size(198, 22);
            this.removeThisErrorToolStripMenuItem.Text = "Remove this error";
            this.removeThisErrorToolStripMenuItem.Click += new System.EventHandler(this.RemoveThisErrorToolStripMenuItem_Click);
            // 
            // alwaysIgnoreThisErrorToolStripMenuItem
            // 
            this.alwaysIgnoreThisErrorToolStripMenuItem.Name = "alwaysIgnoreThisErrorToolStripMenuItem";
            this.alwaysIgnoreThisErrorToolStripMenuItem.Size = new System.Drawing.Size(198, 22);
            this.alwaysIgnoreThisErrorToolStripMenuItem.Text = "Always ignore this error";
            this.alwaysIgnoreThisErrorToolStripMenuItem.Click += new System.EventHandler(this.AlwaysIgnoreThisErrorToolStripMenuItem_Click);
            // 
            // checkBox_applyPatches
            // 
            this.checkBox_applyPatches.AutoSize = true;
            this.checkBox_applyPatches.Location = new System.Drawing.Point(0, 304);
            this.checkBox_applyPatches.Name = "checkBox_applyPatches";
            this.checkBox_applyPatches.Size = new System.Drawing.Size(93, 17);
            this.checkBox_applyPatches.TabIndex = 8;
            this.checkBox_applyPatches.Text = "Apply patches";
            this.checkBox_applyPatches.UseVisualStyleBackColor = true;
            this.checkBox_applyPatches.CheckedChanged += new System.EventHandler(this.checkBox_applyPatches_CheckedChanged);
            // 
            // MainForm
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 13F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.tabControl1);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.Manual;
            this.Text = "Form1";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.Form1_FormClosing);
            this.tabControl1.ResumeLayout(false);
            this.tabPage_DataCollection.ResumeLayout(false);
            this.splitContainer1.Panel1.ResumeLayout(false);
            this.splitContainer1.Panel1.PerformLayout();
            this.splitContainer1.Panel2.ResumeLayout(false);
            this.splitContainer1.Panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)(this.splitContainer1)).EndInit();
            this.splitContainer1.ResumeLayout(false);
            this.tabPage_Report.ResumeLayout(false);
            ((System.ComponentModel.ISupportInitialize)(this.dataGridView_report)).EndInit();
            this.contextMenuStrip1.ResumeLayout(false);
            this.ResumeLayout(false);

        }

        #endregion

        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage_DataCollection;
        private System.Windows.Forms.TabPage tabPage_Report;
        private System.Windows.Forms.SplitContainer splitContainer1;
        private System.Windows.Forms.CheckBox checkBox_alwaysOnTop;
        private System.Windows.Forms.CheckBox checkBox_ignoreHttpsError;
        private System.Windows.Forms.Button button_validateProject;
        private System.Windows.Forms.Button button_SelectProject;
        private System.Windows.Forms.TextBox textBox_logText;
        private System.Windows.Forms.DataGridView dataGridView_report;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialog1;
        private System.Windows.Forms.CheckBox checkBox_skipSchemaProblems;
        private System.Windows.Forms.CheckBox checkBox_reformatJson;
        private System.Windows.Forms.ContextMenuStrip contextMenuStrip1;
        private System.Windows.Forms.ToolStripMenuItem removeThisErrorToolStripMenuItem;
        private System.Windows.Forms.ToolStripMenuItem alwaysIgnoreThisErrorToolStripMenuItem;
        private System.Windows.Forms.CheckedListBox checkedListBox_validators;
        private System.Windows.Forms.CheckBox checkBox_showPreview;
        private System.Windows.Forms.Button button_validateAll;
        private System.Windows.Forms.CheckBox checkBox_saveFiles;
        private System.Windows.Forms.CheckBox checkBox_saveReport;
        private System.Windows.Forms.Button button_selectAssemblyFolder;
        private System.Windows.Forms.CheckBox checkBox_vsCode;
        private System.Windows.Forms.CheckBox checkBox_applyPatches;
    }
}

