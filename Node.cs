using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;

namespace csmon.Models.Db
{
// ReSharper disableInconsistentNaming

    public class Node
    {
        [Key]
        [MaxLength(50)]
        public string Ip { get; set; }
        [NotMapped] public string IpCut => string.Join('.', Ip.Split('.').Take(2)) + ".*.*";
        public string Network { get; set; }
        public float Latitude { get; set; }
        public float Longitude { get; set; }
        public DateTime ModifyTime { get; set; } = DateTime.Now;
        public string City { get; set; }
        public string Region { get; set; }
        public string Country_name { get; set; }
        public string Org { get; set; }
        public string Version { get; set; }
        public string Platform { get; set; }
        public int Size { get; set; }
        public string Country { get; set; }
    }
}
