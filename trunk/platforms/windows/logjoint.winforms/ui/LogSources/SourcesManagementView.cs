﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using LogJoint.UI.Presenters.SourcesManager;

namespace LogJoint.UI
{
	public partial class SourcesManagementView : UserControl, IView
	{
		public SourcesManagementView()
		{
			InitializeComponent();
		}

		public void SetPresenter(IPresenterEvents presenter)
		{
			this.presenter = presenter;
		}

		public SourcesListView SourcesListView
		{
			get { return sourcesListView; }
		}

		bool IView.ShowDeletionConfirmationDialog(int nrOfSourcesToDelete)
		{
			return MessageBox.Show(
				string.Format("You are about to remove {0} log source(s).\nAre you sure?", nrOfSourcesToDelete),
				this.Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question) == DialogResult.Yes;
		}

		void IView.ShowMRUMenu(List<MRUMenuItem> items)
		{
			mruContextMenuStrip.Items.Clear();
			foreach (var item in items)
			{
				mruContextMenuStrip.Items.Add(new ToolStripMenuItem(item.Text)
				{
					Tag = item.ID,
					Enabled = !item.Disabled
				});
			}
			mruContextMenuStrip.Show(recentButton, new Point(0, recentButton.Height));
		}

		void IView.ShowMRUOpeningFailurePopup()
		{
			MessageBox.Show(string.Format("Failed to open file"), 
				"Error", MessageBoxButtons.OK, MessageBoxIcon.Warning);
		}

		void IView.EnableDeleteAllSourcesButton(bool enable)
		{
			deleteAllButton.Enabled = enable;
		}

		void IView.EnableDeleteSelectedSourcesButton(bool enable)
		{
			deleteButton.Enabled = enable;
		}

		void IView.EnableTrackChangesCheckBox(bool enable)
		{
			trackChangesCheckBox.Enabled = enable;
		}

		void IView.SetTrackingChangesCheckBoxState(TrackingChangesCheckBoxState state)
		{
			CheckState newState;
			if (state == TrackingChangesCheckBoxState.Indeterminate)
				newState = CheckState.Indeterminate;
			else if (state == TrackingChangesCheckBoxState.Checked)
				newState = CheckState.Checked;
			else
				newState = CheckState.Unchecked;

			if (trackChangesCheckBox.CheckState != newState)
				trackChangesCheckBox.CheckState = newState;
		}

		private void addNewLogButton_Click(object sender, EventArgs e)
		{
			presenter.OnAddNewLogButtonClicked();
		}

		private void deleteButton_Click(object sender, EventArgs e)
		{
			presenter.OnDeleteSelectedLogSourcesButtonClicked();
		}

		private void deleteAllButton_Click(object sender, EventArgs e)
		{
			presenter.OnDeleteAllLogSourcesButtonClicked();
		}

		private void recentButton_Click(object sender, EventArgs e)
		{
			presenter.OnMRUButtonClicked();
		}

		private void mruContextMenuStrip_ItemClicked(object sender, ToolStripItemClickedEventArgs e)
		{
			// Hide the menu here to simplify debugging. Without Hide() the menu remains 
			// on the screen if execution stops at a breakepoint.
			mruContextMenuStrip.Hide();

			string itemId = e.ClickedItem.Tag as string;
			presenter.OnMRUMenuItemClicked(itemId);
		}

		private void trackChangesCheckBox_Click(object sender, EventArgs e)
		{
			bool value = trackChangesCheckBox.CheckState == CheckState.Unchecked;
			presenter.OnTrackingChangesCheckBoxChecked(value);
		}

		IPresenterEvents presenter;

	}
}
