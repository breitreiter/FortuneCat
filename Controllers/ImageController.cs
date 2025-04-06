using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace FortuneService.Controllers
{
    [Route("[controller]")]
    [ApiController]
    public class ImageController : ControllerBase
    {
        private readonly ILogger<TextController> _logger;
        private readonly ScreenContents _contents;

        public ImageController(ILogger<TextController> logger, ScreenContents contents)
        {
            _logger = logger;
            _contents = contents;
        }

        [EnableCors("corsapp")]
        [HttpGet(Name = "GetImageUrl")]
        public string Get()
        {
            if (!_contents.Seen)
            {
                lock(_contents) 
                {
                    _contents.Seen = true;
                }
            }
            return _contents.ImageUrl;
        }
    }
}
