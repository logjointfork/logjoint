using System;
using System.Collections.Generic;

namespace LogJoint.UI.Presenters.FiltersListBox
{
	public interface IPresenter
	{
		FiltersList FiltersList { get; }
		event EventHandler FilterChecked;
		event EventHandler SelectionChanged;
		void SelectFilter(Filter filter);
		IEnumerable<Filter> SelectedFilters { get; }
		void UpdateView();
	};

	public interface IView
	{
		void BeginUpdate();
		void EndUpdate();

		IViewItem CreateItem(Filter filter, string key);

		int Count { get; }
		IViewItem GetItem(int index);

		void RemoveAt(int index);
		void Remove(IViewItem item);
		void Insert(int index, IViewItem item);
		int GetItemIndexByKey(string key);

		IEnumerable<IViewItem> SelectedItems { get; }

		void SetEnabled(bool value);
	};

	public enum ViewItemImageType
	{
		None,
		Include,
		Exclude
	};

	public interface IViewItem
	{
		Filter Filter { get; }
		string Text { get; set; }
		bool Checked { get; set; }
		bool Selected { get; set; }
		void SetImageType(ViewItemImageType imageType);
		void SetSubText(string text);
	};

	[Flags]
	public enum ContextMenuItem
	{
		None = 0,
		FilterEnabled = 1,
		Properties = 2,
	};

	public interface IPresenterEvents
	{
		void OnSelectionChanged();
		void OnItemChecked(IViewItem item);
		void OnContextMenuOpening(out ContextMenuItem enabledItems, out ContextMenuItem checkedItems);
		void OnFilterEnabledMenuItemClicked();
		void OnPropertiesMenuItemClicked();
		void OnENTERPressed();
	};
};