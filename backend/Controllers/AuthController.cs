using Microsoft.AspNetCore.Mvc;
using backend.Models;
using backend.Contracts.Requests;
using Microsoft.EntityFrameworkCore;

namespace backend
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly BmsContext _context;
        public AuthController(BmsContext context)
        {
            _context = context;
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u =>
                u.Login != null && u.Password != null &&
                u.Login == request.Login &&
                u.Password == request.Password);

            if (user == null)
                return Unauthorized(new { message = "Неверный логин или пароль" });
            
            user.Password = null;
            return Ok(user);
        }
    }
} 