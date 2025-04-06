namespace FortuneService
{
    public class ScreenContents
    {
        public const string DEFAULT_TEXT = "A stopped clock is right twice a day.";
        public const string DEFAULT_CAT_URL = "https://dreamlands.org/fortunecat/default_cat.png";

        private string _text;
        private string _imageUrl;
        private bool _seen;
        private DateTime _lastImageUpdate;

        public string Text
        {
            get
            {
                return _text;
            }
            set
            {
                if (value == null)
                    _text = DEFAULT_TEXT;
                else
                    _text = value;
            }
        }

        public string ImageUrl
        {
            get
            {
                return _imageUrl;
            }
            set
            {
                if (value == null)
                    _imageUrl = DEFAULT_CAT_URL;
                else
                {
                    _imageUrl = value;
                    _lastImageUpdate = DateTime.Now;
                }
            }
        }

        public bool Seen
        {
            get {
                return _seen; 
            }
            set { 
                _seen = value;
            }
        }

        public DateTime LastImageUpdate
        {
            get { return _lastImageUpdate; }
            set { _lastImageUpdate = value; }
        }

        public ScreenContents()
        {
            _text = DEFAULT_TEXT;
            _imageUrl = DEFAULT_CAT_URL;
            _seen = false;
            _lastImageUpdate = DateTime.MinValue;
        }
    }
}
