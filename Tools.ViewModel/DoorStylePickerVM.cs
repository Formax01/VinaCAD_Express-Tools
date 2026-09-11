using PrMVVMCore;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Tools.Model;

namespace Tools.ViewModel
{
    public sealed class DoorStylePickerVM : BaseViewModel
    {
        private const int PageSize = 16;
        private readonly IReadOnlyList<DoorStyleModel> _allStyles;
        private DoorStyleModel? _selectedStyle;
        private int _pageIndex;

        public ObservableCollection<DoorStyleModel> Styles { get; } = new ObservableCollection<DoorStyleModel>();
        public string AssetsPath { get; }

        public DoorStyleModel? SelectedStyle
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

        public DoorStylePickerVM(DoorStyleCatalog catalog, DoorStyleModel? initialStyle = null)
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
            foreach (DoorStyleModel style in _allStyles.Skip(_pageIndex * PageSize).Take(PageSize))
                Styles.Add(style);
            SelectedStyle = selectedId == null
                ? Styles.FirstOrDefault()
                : Styles.FirstOrDefault(style => string.Equals(style.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                  ?? Styles.FirstOrDefault();
            OnPropertyChanged(nameof(PageText));
            OnPropertyChanged(nameof(CanPrevious));
            OnPropertyChanged(nameof(CanNext));
        }
    }
}
