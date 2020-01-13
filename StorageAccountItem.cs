namespace ImageTagging
{
    public abstract class StorageAccountItem
    {
        public string Path { get; set; }

        protected StorageAccountItem(string path)
        {
            Path = path;
        }
    }

    public class Image: StorageAccountItem
    {
        public Image(string path) : base(path)
        {
        }
    }

    public class Directory: StorageAccountItem
    {
        public Directory(string path) : base(path)
        {
        }
    }
}