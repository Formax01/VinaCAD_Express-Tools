using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Tools.Model;

namespace Tools.ViewModel
{
    public sealed class WindowStylePickerVM : BaseViewModel
    {
        private const int PageSize = 16;
        private readonly IReadOnlyList<WindowStyleModel> _allStyles;
        private WindowStyleModel? _selectedStyle;
        private int _pageIndex;

        public ObservableCollection<WindowStyleModel> Styles { get; } = new ObservableCollection<WindowStyleModel>();
        public string AssetsPath { get; }

        public WindowStyleModel? SelectedStyle
        {
            get => _selectedStyle;
            set
            {
                if (ReferenceEquals(_selectedStyle, value)) return;
                _selectedStyle = value;
                OnPropertyChanged(nameof(SelectedStyle));
            }
        }

        public string PageText => $"{_pageIndex + 1}/{PageCount}";
        public bool CanPrevious => _pageIndex > 0;
        public bool CanNext => _pageIndex + 1 < PageCount;
        private int PageCount => Math.Max(1, (int)Math.Ceiling(_allStyles.Count / (double)PageSize));

        public WindowStylePickerVM(WindowStyleCatalog catalog, WindowStyleModel? initialStyle = null)
        {
            _allStyles = catalog.Styles;
            AssetsPath = _allStyles.FirstOrDefault()?.AssetPath is string firstPath
                ? System.IO.Path.GetDirectoryName(firstPath) ?? string.Empty
                : string.Empty;
            int initialIndex = initialStyle == null
                ? 0
                : Math.Max(0, _allStyles.ToList().FindIndex(style =>
                    string.Equals(style.Id, initialStyle.Id, StringComparison.OrdinalIgnoreCase)));
            _pageIndex = initialIndex / PageSize;
            LoadPage(initialStyle?.Id);
        }

        public void PreviousPage()
        {
            if (!CanPrevious) return;
            _pageIndex--;
            LoadPage(null);
        }

        public void NextPage()
        {
            if (!CanNext) return;
            _pageIndex++;
            LoadPage(null);
        }

        private void LoadPage(string? selectedId)
        {
            Styles.Clear();
            foreach (WindowStyleModel style in _allStyles.Skip(_pageIndex * PageSize).Take(PageSize))
                Styles.Add(style);
            while (Styles.Count < PageSize)
                Styles.Add(new WindowStyleModel());
            SelectedStyle = selectedId == null
                ? Styles.FirstOrDefault(style => !string.IsNullOrEmpty(style.AssetPath))
                : Styles.FirstOrDefault(style => string.Equals(style.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                  ?? Styles.FirstOrDefault(style => !string.IsNullOrEmpty(style.AssetPath));
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(CanPrevious));
            OnPropertyChanged(nameof(CanNext));
        }
    }
}
