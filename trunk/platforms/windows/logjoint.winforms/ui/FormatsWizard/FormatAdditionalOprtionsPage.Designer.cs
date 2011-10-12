namespace LogJoint.UI
{
	partial class FormatAdditionalOptionsPage
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

		#region Component Designer generated code

		/// <summary> 
		/// Required method for Designer support - do not modify 
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.patternsListBox = new System.Windows.Forms.ListBox();
			this.label1 = new System.Windows.Forms.Label();
			this.panel1 = new System.Windows.Forms.Panel();
			this.extensionTextBox = new System.Windows.Forms.TextBox();
			this.addExtensionButton = new System.Windows.Forms.Button();
			this.removeExtensionButton = new System.Windows.Forms.Button();
			this.tableLayoutPanel1 = new System.Windows.Forms.TableLayoutPanel();
			this.label3 = new System.Windows.Forms.Label();
			this.label4 = new System.Windows.Forms.Label();
			this.label5 = new System.Windows.Forms.Label();
			this.label6 = new System.Windows.Forms.Label();
			this.label7 = new System.Windows.Forms.Label();
			this.label8 = new System.Windows.Forms.Label();
			this.label9 = new System.Windows.Forms.Label();
			this.label10 = new System.Windows.Forms.Label();
			this.encodingComboBox = new System.Windows.Forms.ComboBox();
			this.panel1.SuspendLayout();
			this.tableLayoutPanel1.SuspendLayout();
			this.SuspendLayout();
			// 
			// patternsListBox
			// 
			this.patternsListBox.Dock = System.Windows.Forms.DockStyle.Fill;
			this.patternsListBox.FormattingEnabled = true;
			this.patternsListBox.IntegralHeight = false;
			this.patternsListBox.Location = new System.Drawing.Point(0, 0);
			this.patternsListBox.Name = "patternsListBox";
			this.patternsListBox.SelectionMode = System.Windows.Forms.SelectionMode.MultiExtended;
			this.patternsListBox.Size = new System.Drawing.Size(122, 106);
			this.patternsListBox.TabIndex = 3;
			this.patternsListBox.SelectedIndexChanged += new System.EventHandler(this.extensionsListBox_SelectedIndexChanged);
			// 
			// label1
			// 
			this.label1.AutoSize = true;
			this.label1.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
			this.label1.Location = new System.Drawing.Point(13, 12);
			this.label1.Name = "label1";
			this.label1.Size = new System.Drawing.Size(263, 13);
			this.label1.TabIndex = 15;
			this.label1.Text = "Your log files may have these name patterns:";
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.patternsListBox);
			this.panel1.Location = new System.Drawing.Point(27, 41);
			this.panel1.Name = "panel1";
			this.panel1.Size = new System.Drawing.Size(122, 106);
			this.panel1.TabIndex = 3;
			// 
			// extensionTextBox
			// 
			this.extensionTextBox.Location = new System.Drawing.Point(27, 153);
			this.extensionTextBox.Name = "extensionTextBox";
			this.extensionTextBox.Size = new System.Drawing.Size(122, 21);
			this.extensionTextBox.TabIndex = 1;
			this.extensionTextBox.TextChanged += new System.EventHandler(this.extensionTextBox_TextChanged);
			// 
			// addExtensionButton
			// 
			this.addExtensionButton.Location = new System.Drawing.Point(155, 153);
			this.addExtensionButton.Name = "addExtensionButton";
			this.addExtensionButton.Size = new System.Drawing.Size(75, 23);
			this.addExtensionButton.TabIndex = 2;
			this.addExtensionButton.Text = "Add";
			this.addExtensionButton.UseVisualStyleBackColor = true;
			this.addExtensionButton.Click += new System.EventHandler(this.addExtensionButton_Click);
			// 
			// removeExtensionButton
			// 
			this.removeExtensionButton.Location = new System.Drawing.Point(155, 41);
			this.removeExtensionButton.Name = "removeExtensionButton";
			this.removeExtensionButton.Size = new System.Drawing.Size(75, 23);
			this.removeExtensionButton.TabIndex = 4;
			this.removeExtensionButton.Text = "Remove";
			this.removeExtensionButton.UseVisualStyleBackColor = true;
			this.removeExtensionButton.Click += new System.EventHandler(this.removeExtensionButton_Click);
			// 
			// tableLayoutPanel1
			// 
			this.tableLayoutPanel1.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
						| System.Windows.Forms.AnchorStyles.Right)));
			this.tableLayoutPanel1.ColumnCount = 2;
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle());
			this.tableLayoutPanel1.ColumnStyles.Add(new System.Windows.Forms.ColumnStyle(System.Windows.Forms.SizeType.Percent, 100F));
			this.tableLayoutPanel1.Controls.Add(this.label3, 0, 0);
			this.tableLayoutPanel1.Controls.Add(this.label4, 0, 1);
			this.tableLayoutPanel1.Controls.Add(this.label5, 0, 2);
			this.tableLayoutPanel1.Controls.Add(this.label6, 1, 0);
			this.tableLayoutPanel1.Controls.Add(this.label7, 1, 1);
			this.tableLayoutPanel1.Controls.Add(this.label8, 1, 2);
			this.tableLayoutPanel1.Location = new System.Drawing.Point(253, 62);
			this.tableLayoutPanel1.Name = "tableLayoutPanel1";
			this.tableLayoutPanel1.RowCount = 3;
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.RowStyles.Add(new System.Windows.Forms.RowStyle());
			this.tableLayoutPanel1.Size = new System.Drawing.Size(217, 140);
			this.tableLayoutPanel1.TabIndex = 22;
			// 
			// label3
			// 
			this.label3.AutoSize = true;
			this.label3.Location = new System.Drawing.Point(3, 0);
			this.label3.Name = "label3";
			this.label3.Size = new System.Drawing.Size(11, 13);
			this.label3.TabIndex = 0;
			this.label3.Text = "-";
			// 
			// label4
			// 
			this.label4.AutoSize = true;
			this.label4.Location = new System.Drawing.Point(3, 31);
			this.label4.Name = "label4";
			this.label4.Size = new System.Drawing.Size(11, 13);
			this.label4.TabIndex = 1;
			this.label4.Text = "-";
			// 
			// label5
			// 
			this.label5.AutoSize = true;
			this.label5.Location = new System.Drawing.Point(3, 62);
			this.label5.Name = "label5";
			this.label5.Size = new System.Drawing.Size(11, 13);
			this.label5.TabIndex = 2;
			this.label5.Text = "-";
			// 
			// label6
			// 
			this.label6.AutoSize = true;
			this.label6.Location = new System.Drawing.Point(20, 0);
			this.label6.Margin = new System.Windows.Forms.Padding(3, 0, 3, 5);
			this.label6.Name = "label6";
			this.label6.Size = new System.Drawing.Size(192, 26);
			this.label6.TabIndex = 3;
			this.label6.Text = "Patterns may contain wildcards (?, *). For insntance, MyApp-*.log";
			// 
			// label7
			// 
			this.label7.AutoSize = true;
			this.label7.Location = new System.Drawing.Point(20, 31);
			this.label7.Margin = new System.Windows.Forms.Padding(3, 0, 3, 5);
			this.label7.Name = "label7";
			this.label7.Size = new System.Drawing.Size(173, 26);
			this.label7.TabIndex = 3;
			this.label7.Text = "You can define only extentions by adding *.MyExt\r\n";
			// 
			// label8
			// 
			this.label8.AutoSize = true;
			this.label8.Location = new System.Drawing.Point(20, 62);
			this.label8.Margin = new System.Windows.Forms.Padding(3, 0, 3, 5);
			this.label8.Name = "label8";
			this.label8.Size = new System.Drawing.Size(186, 52);
			this.label8.TabIndex = 3;
			this.label8.Text = "Empty list means that LogJoint won\'t filter out irrelevant files when\r\nyou open y" +
				"ou log; *.* is assumed by default.\r\n";
			// 
			// label9
			// 
			this.label9.AutoSize = true;
			this.label9.Location = new System.Drawing.Point(256, 41);
			this.label9.Name = "label9";
			this.label9.Size = new System.Drawing.Size(34, 13);
			this.label9.TabIndex = 23;
			this.label9.Text = "Note:";
			// 
			// label10
			// 
			this.label10.AutoSize = true;
			this.label10.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(204)));
			this.label10.Location = new System.Drawing.Point(13, 208);
			this.label10.Name = "label10";
			this.label10.Size = new System.Drawing.Size(191, 13);
			this.label10.TabIndex = 15;
			this.label10.Text = "Your log files have this encoding:";
			// 
			// encodingComboBox
			// 
			this.encodingComboBox.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
			this.encodingComboBox.FormattingEnabled = true;
			this.encodingComboBox.Location = new System.Drawing.Point(27, 230);
			this.encodingComboBox.Name = "encodingComboBox";
			this.encodingComboBox.Size = new System.Drawing.Size(228, 21);
			this.encodingComboBox.TabIndex = 24;
			// 
			// FormatAdditionalOptionsPage
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(96F, 96F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Dpi;
			this.Controls.Add(this.encodingComboBox);
			this.Controls.Add(this.label9);
			this.Controls.Add(this.tableLayoutPanel1);
			this.Controls.Add(this.removeExtensionButton);
			this.Controls.Add(this.addExtensionButton);
			this.Controls.Add(this.extensionTextBox);
			this.Controls.Add(this.panel1);
			this.Controls.Add(this.label10);
			this.Controls.Add(this.label1);
			this.Font = new System.Drawing.Font("Tahoma", 8.25F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
			this.Name = "FormatAdditionalOptionsPage";
			this.Size = new System.Drawing.Size(482, 315);
			this.panel1.ResumeLayout(false);
			this.tableLayoutPanel1.ResumeLayout(false);
			this.tableLayoutPanel1.PerformLayout();
			this.ResumeLayout(false);
			this.PerformLayout();

		}

		#endregion

		private System.Windows.Forms.Label label1;
		private System.Windows.Forms.Panel panel1;
		private System.Windows.Forms.TextBox extensionTextBox;
		private System.Windows.Forms.Button addExtensionButton;
		private System.Windows.Forms.Button removeExtensionButton;
		private System.Windows.Forms.TableLayoutPanel tableLayoutPanel1;
		private System.Windows.Forms.Label label3;
		private System.Windows.Forms.Label label4;
		private System.Windows.Forms.Label label5;
		private System.Windows.Forms.Label label6;
		private System.Windows.Forms.Label label7;
		private System.Windows.Forms.Label label8;
		private System.Windows.Forms.Label label9;
		private System.Windows.Forms.Label label10;
		private System.Windows.Forms.ComboBox encodingComboBox;
		private System.Windows.Forms.ListBox patternsListBox;
	}
}