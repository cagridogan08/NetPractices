using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace DockingManagerTestApplication
{
    public class CodeEditorViewModel : INotifyPropertyChanged
    {
        private string _title;
        private string _content;
        private bool _isDirty;
        private string _filePath;

        public CodeEditorViewModel(string title, string content)
        {
            _title = title;
            _content = content;
            _isDirty = false;
        }

        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(); }
        }

        public string Content
        {
            get => _content;
            set
            {
                if (_content != value)
                {
                    _content = value;
                    IsDirty = true;
                    OnPropertyChanged();
                }
            }
        }

        public bool IsDirty
        {
            get => _isDirty;
            set { _isDirty = value; OnPropertyChanged(); }
        }

        public string FilePath
        {
            get => _filePath;
            set { _filePath = value; OnPropertyChanged(); }
        }

        public string ContentId => GetHashCode().ToString();

        public event PropertyChangedEventHandler PropertyChanged;
        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
