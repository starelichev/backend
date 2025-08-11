using Microsoft.AspNetCore.Mvc;
using backend.Models;
using backend.Contracts;
using Microsoft.EntityFrameworkCore;

namespace backend.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AdminController : ControllerBase
    {
        private readonly BmsContext _context;

        public AdminController(BmsContext context)
        {
            _context = context;
        }

        // GET: api/Admin/users
        [HttpGet("users")]
        public async Task<ActionResult<List<AdminUserResponse>>> GetUsers()
        {
            try
            {
                var users = await _context.Users
                    .Select(u => new AdminUserResponse
                    {
                        Id = u.Id,
                        Login = u.Login,
                        Password = u.Password,
                        Email = u.Email,
                        Name = u.Name,
                        Surname = u.Surname,
                        Patronymic = u.Patronymic,
                        Phone = u.Phone
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ошибка при получении пользователей", error = ex.Message });
            }
        }

        // POST: api/Admin/users
        [HttpPost("users")]
        public async Task<ActionResult<AdminUserResponse>> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Login == request.Login);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "Пользователь с таким логином уже существует" });
                }
                
                if (!string.IsNullOrEmpty(request.Email))
                {
                    var existingEmail = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                    if (existingEmail != null)
                    {
                        return BadRequest(new { message = "Пользователь с таким email уже существует" });
                    }
                }
                
                var user = new User
                {
                    Login = request.Login,
                    Password = request.Password,
                    Email = request.Email,
                    Surname = request.Surname,
                    Phone = request.Phone,
                    Name = request.Name,
                    Patronymic = request.Patronymic,
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();
                
                var response = new AdminUserResponse
                {
                    Id = user.Id,
                    Login = user.Login,
                    Password = user.Password,
                    Email = user.Email,
                    Name = user.Name,
                    Surname = user.Surname,
                    Patronymic = user.Patronymic,
                    Phone = user.Phone
                };

                return CreatedAtAction(nameof(GetUsers), response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ошибка при создании пользователя", error = ex.Message });
            }
        }

        // PUT: api/Admin/users/{id}
        [HttpPut("users/{id}")]
        public async Task<ActionResult<AdminUserResponse>> UpdateUser(int id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }
                
                if (request.Login != user.Login)
                {
                    var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Login == request.Login);
                    if (existingUser != null)
                    {
                        return BadRequest(new { message = "Пользователь с таким логином уже существует" });
                    }
                }
                
                if (request.Email != user.Email)
                {
                    var existingEmail = await _context.Users.FirstOrDefaultAsync(u => u.Email == request.Email);
                    if (existingEmail != null)
                    {
                        return BadRequest(new { message = "Пользователь с таким email уже существует" });
                    }
                }
                
                if (!string.IsNullOrEmpty(request.Login))
                {
                    user.Login = request.Login;
                }
                
                if (!string.IsNullOrEmpty(request.Password))
                {
                    user.Password = request.Password;
                }
                
                if (!string.IsNullOrEmpty(request.Email))
                {
                    user.Email = request.Email;
                }
                
                if (!string.IsNullOrEmpty(request.Name))
                {
                    user.Name = request.Name;
                }
                
                if (!string.IsNullOrEmpty(request.Surname))
                {
                    user.Surname = request.Surname;
                }
                
                if (!string.IsNullOrEmpty(request.Patronymic))
                {
                    user.Patronymic = request.Patronymic;
                }
                
                if (!string.IsNullOrEmpty(request.Phone))
                {
                    user.Phone = request.Phone;
                }

                await _context.SaveChangesAsync();
                
                var response = new AdminUserResponse
                {
                    Id = user.Id,
                    Login = user.Login,
                    Password = user.Password,
                    Email = user.Email,
                    Name = user.Name,
                    Surname = user.Surname,
                    Patronymic = user.Patronymic,
                    Phone = user.Phone
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ошибка при обновлении пользователя", error = ex.Message });
            }
        }

        // DELETE: api/Admin/users/{id}
        [HttpDelete("users/{id}")]
        public async Task<ActionResult> DeleteUser(int id)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == id);

                if (user == null)
                {
                    return NotFound(new { message = "Пользователь не найден" });
                }
                
                _context.Users.Remove(user);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Пользователь успешно удален" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Ошибка при удалении пользователя", error = ex.Message });
            }
        }
    }
} 