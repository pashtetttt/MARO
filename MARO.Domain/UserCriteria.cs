﻿using System.Text.Json.Serialization;

namespace MARO.Domain
{
    public class UserCriteria
    {
        public string UserId { get; set; } = null!;
        public int CriterionId { get; set; }

        [JsonIgnore]
        public User User { get; set; } = null!;
        [JsonIgnore]
        public Criterion Criterion { get; set; } = null!;
    }
}
