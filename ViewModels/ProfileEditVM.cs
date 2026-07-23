using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace UniConnect.ViewModels
{
    public class ProfileEditVM
    {
        [Phone]
        [StringLength(20)]
        [Display(Name = "Phone Number")]
        public string? PhoneNumber { get; set; }

        [Display(Name = "Profile Picture")]
        public IFormFile? ProfilePicture { get; set; }
    }
}
