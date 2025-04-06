using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace FortuneService.Controllers
{
    [ApiController]
    [Route("[controller]")]
    public class TextController : ControllerBase
    {
        private readonly ILogger<TextController> _logger;
        private readonly ScreenContents _contents;

        public TextController(ILogger<TextController> logger, ScreenContents contents)
        {
            _logger = logger;
            _contents = contents;
        }

        [EnableCors("corsapp")]
        [HttpGet(Name = "GetText")]
        public string Get()
        {
            if (!_contents.Seen)
            {
                lock (_contents)
                {
                    _contents.Seen = true;
                }
            }

            return _contents.Text;
        }

    }
}
