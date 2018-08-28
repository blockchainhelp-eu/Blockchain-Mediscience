using System.ComponentModel.DataAnnotations;

namespace csmon.Models.Db
{
    public class Smart
    {
        [Key]
        public string Address { get; set; }
        public string Network { get; set; }
    }
}
