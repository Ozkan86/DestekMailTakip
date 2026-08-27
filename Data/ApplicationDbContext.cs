using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using task_list.Models;

namespace task_list.Data;

// Sadece ASP.NET Core Identity tablolari (AspNetUsers, AspNetRoles, ...) icin kullanilir.
// Mail/ek/bayrak verileri ADO.NET ile MailRepository uzerinden yonetilir.
public class ApplicationDbContext : IdentityDbContext<ApplicationUser>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
}
