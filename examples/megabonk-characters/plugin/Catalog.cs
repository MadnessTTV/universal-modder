namespace UmMegabonkStudio
{
    // Index equals the enum value published for the 1.0.69-era tools. tests/test_megabonk.py
    // checks these arrays against um/megabonk.py. A game update can renumber them.
    static class Catalog
    {
        public static readonly string[] Stats =
        {
            "MaxHealth",
            "HealthRegen",
            "Shield",
            "Thorns",
            "Armor",
            "Evasion",
            "Evolve",
            "DamageReductionMultiplier",
            "DamageCooldownMultiplier",
            "SizeMultiplier",
            "DurationMultiplier",
            "ProjectileSpeedMultiplier",
            "DamageMultiplier",
            "Unused0",
            "EffectDurationMultiplier",
            "AttackSpeed",
            "Projectiles",
            "Lifesteal",
            "CritChance",
            "CritDamage",
            "FireDamage",
            "IceDamage",
            "LightningDamage",
            "EliteDamageMultiplier",
            "KnockbackMultiplier",
            "MoveSpeedMultiplier",
            "JumpHeight",
            "FallDamageReduction",
            "Slam",
            "PickupRange",
            "Luck",
            "GoldIncreaseMultiplier",
            "XpIncreaseMultiplier",
            "ChestIncreaseMultiplier",
            "ChestPriceMultiplier",
            "ShopPriceReduction",
            "Holiness",
            "Wickedness",
            "Difficulty",
            "EliteSpawnIncrease",
            "PowerupBoostMultiplier",
            "PowerupChance",
            "BurnChance",
            "FreezeChance",
            "WeaponBurstCooldown",
            "ProjectileBounces",
            "ExtraJumps",
            "Overheal",
            "HealingMultiplier",
            "SilverIncreaseMultiplier",
            "EnemyAmountMultiplier",
            "EnemySizeMultiplier",
            "EnemySpeedMultiplier",
            "EnemyHpMultiplier",
            "EnemyDamageMultiplier",
            "EnemyScalingMultiplier",
            "PoisonDamageMultiplier",
        };

        public static readonly string[] Passives =
        {
            "Bullseye",
            "RngBlessing",
            "SpeedDemon",
            "Reinforced",
            "Flowstate",
            "CritHappens",
            "Warrior",
            "Flex",
            "WallClimb",
            "None",
            "Float",
            "Enduring",
            "Plague",
            "Quantum",
            "Shadowstep",
            "Gamba",
            "Vampire",
            "Curse",
            "Stonks",
            "LockIn",
            "Zap",
        };

        public static readonly string[] Characters =
        {
            "Fox",
            "Calcium",
            "SirOofie",
            "Cl4nk",
            "Megachad",
            "Ogre",
            "Robinette",
            "Athena",
            "Birdo",
            "Bush",
            "Bandit",
            "Monke",
            "Noelle",
            "TonyMcZoom",
            "Amog",
            "Spaceman",
            "Ninja",
            "Vlad",
            "Dicehead",
            "SirChadwell",
        };

        public static readonly string[] Weapons =
        {
            "FireStaff",
            "Bone",
            "Sword",
            "Revolver",
            "Aura",
            "Axe",
            "Bow",
            "Aegis",
            "Test",
            "LightningStaff",
            "Flamewalker",
            "Rockets",
            "Bananarang",
            "Tornado",
            "Dexecutioner",
            "Sniper",
            "Frostwalker",
            "SpaceNoodle",
            "DragonsBreath",
            "Chunkers",
            "Mine",
            "PoisonFlask",
            "BlackHole",
            "Katana",
            "BloodMagic",
            "BluetoothDagger",
            "Dice",
            "HeroSword",
            "CorruptSword",
            "Shotgun",
            "Scythe",
        };

        public static readonly string[] ModifyTypes =
        {
            "Addition",
            "Multiplication",
            "Flat",
        };

        public static int IndexOf(string[] table, string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return -1;
            for (int i = 0; i < table.Length; i++)
            {
                if (string.Equals(table[i], name.Trim(), System.StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }
    }
}
