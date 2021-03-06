﻿using System;
using System.Collections.Generic;
using System.Data.SQLite;
using DamageMeter.Database.Structures;
using Data;
using Tera.Game;
using Skill = DamageMeter.Database.Structures.Skill;

namespace DamageMeter.Database
{
    public class Database
    {

        /*
         *  The enum value NEED to be set manually
         *  without that, converting the enum to int will cause massive weird bug, like:
         *  https://github.com/neowutran/ShinraMeter/issues/184
         * */
        public enum Type
        {
            Damage = 1,
            Heal = 2,
            Mana = 3
        }

        private static Database _instance;

        public SQLiteConnection Connexion;

        private Database()
        {
            Connexion = new SQLiteConnection("Data Source=:memory:;Version=3;");
            Connexion.Open();
            Init();
        }

        public static Database Instance => _instance ?? (_instance = new Database());

        private void Init()
        {
            var sql = "create table skills (" +
                      "id INTEGER PRIMARY KEY AUTOINCREMENT NOT NULL," +
                      "amount INTEGER NOT NULL," +
                      "type INTEGER NOT NULL," +
                      "target INTEGER NOT NULL," +
                      "source INTEGER NOT NULL," +
                      "pet_zone INTEGER DEFAULT NULL," +
                      "pet_id INTEGER DEFAULT NULL," +
                      "skill_id INTEGER NOT NULL," +
                      "critic INTEGER NOT NULL," +
                      "direction INTEGER NOT NULL," +
                      "hotdot INTEGER NOT NULL," +
                      "time INTEGER NOT NULL" +
                      "); ";
            var command = new SQLiteCommand(sql, Connexion);
            command.ExecuteNonQuery();

            sql = "CREATE INDEX `index_target` ON `skills` (`target` ASC);";
            command = new SQLiteCommand(sql, Connexion);
            command.ExecuteNonQuery();

            sql = "CREATE INDEX `index_time` ON `skills` (`time` ASC);";
            command = new SQLiteCommand(sql, Connexion);
            command.ExecuteNonQuery();
        }

        public void DeleteAll()
        {
            var sql = "DELETE FROM skills;";
            var command = new SQLiteCommand(sql, Connexion);
            command.ExecuteNonQuery();
        }

        public void DeleteEntity(Entity entity)
        {
            var sql = "DELETE FROM skills WHERE source = $entity OR target = $entity";
            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$entity", entity.Id.Id);
            command.ExecuteNonQuery();
        }

        public void Insert(long amount, Type type, Entity target, Entity source, long skillId, bool hotdot, bool critic,
            long time, Entity petSource, HitDirection direction)
        {
            var sql =
                "INSERT INTO skills (amount, type, target, source, skill_id, hotdot, critic, time, pet_zone, pet_id, direction) VALUES( $amount , $type , $target , $source , $skill_id, $hotdot , $critic , $time, $pet_zone, $pet_id, $direction ) ;";
            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$amount", amount);
            command.Parameters.AddWithValue("$type", (int) type);
            command.Parameters.AddWithValue("$target", target.Id.Id);
            command.Parameters.AddWithValue("$source", source.Id.Id);
            command.Parameters.AddWithValue("$skill_id", skillId);
            command.Parameters.AddWithValue("$critic", critic ? 1 : 0);
            command.Parameters.AddWithValue("$hotdot", hotdot ? 1 : 0);
            command.Parameters.AddWithValue("$time", time);
            command.Parameters.AddWithValue("$direction", direction);

            if (petSource != null)
            {
                var pet = (NpcEntity)petSource;
                command.Parameters.AddWithValue("$pet_zone", pet.Info.HuntingZoneId);
                command.Parameters.AddWithValue("$pet_id", pet.Info.TemplateId);

            }
            else
            {
                command.Parameters.AddWithValue("$pet_id", DBNull.Value);
                command.Parameters.AddWithValue("$pet_zone", DBNull.Value);
            }

            command.ExecuteNonQuery();
        }

        public List<EntityId> AllEntity()
        {
            var entities = new List<EntityId>();
            var sql = "SELECT target, MAX(time) as max_time FROM skills GROUP BY target ORDER BY `max_time` DESC;";
            var command = new SQLiteCommand(sql, Connexion);
            var rdr = command.ExecuteReader();
            while (rdr.Read())
            {
                if (rdr.IsDBNull(0)) return entities;
                entities.Add(new EntityId((ulong) rdr.GetInt64(0)));
            }
            return entities;
        }

        public void DeleteAllWhenTimeBelow(NpcEntity entity)
        {
            if (entity == null)
            {
                DeleteAll();
                return;
            }

            var entityInfo = GlobalInformationEntity(entity, false);
            const string sql = "DELETE FROM skills WHERE time < $time";
            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$time", entityInfo.BeginTime);
            command.ExecuteNonQuery();
        }

        public EntityInformation GlobalInformationEntity(NpcEntity entity, bool timed)
        {
            SQLiteCommand command;

            if (entity == null)
            {
                var sql = "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, source " +
                          "FROM skills " +
                          "WHERE type = $type "+
                          "GROUP BY source; ";

                command = new SQLiteCommand(sql, Connexion);
                command.Parameters.AddWithValue("$type", (int) Type.Damage);
            }
            else
            {
                if (!timed)
                {
                    var sql = "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, source " +
                              "FROM skills " +
                              "WHERE target = $target AND type = $type " +
                              "GROUP BY source; ";

                    command = new SQLiteCommand(sql, Connexion);
                    command.Parameters.AddWithValue("$type", (int) Type.Damage);
                    command.Parameters.AddWithValue("$target", entity.Id.Id);
                }
                else
                {
                    var sql =
                        "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, source " +
                        "FROM skills " +
                        "WHERE time BETWEEN (SELECT MIN(time) FROM skills WHERE target = $target) AND (SELECT MAX(time) FROM skills WHERE target = $target) AND type = $type " +
                        "GROUP BY source; ";
                    command = new SQLiteCommand(sql, Connexion);
                    command.Parameters.AddWithValue("$type", (int) Type.Damage);
                    command.Parameters.AddWithValue("$target", entity.Id.Id);
                }
            }


            var rdr = command.ExecuteReader();
            long sumTotalDamage = 0;
            long minBeginTime = 0;
            long maxEndTime = 0;
            while (rdr.Read())
            {          
               var source = rdr.GetFieldValue<long>(rdr.GetOrdinal("source"));
               var entitySource = NetworkController.Instance.EntityTracker.GetOrNull(new EntityId((ulong)source));
               if (!(entitySource is UserEntity)) continue;
                var totalDamage = rdr.IsDBNull(rdr.GetOrdinal("total_amount"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("total_amount"));
                var beginTime = rdr.IsDBNull(rdr.GetOrdinal("start_time"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("start_time"));
                var endTime = rdr.IsDBNull(rdr.GetOrdinal("end_time"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("end_time"));

                sumTotalDamage += totalDamage;

                if (minBeginTime == 0 || beginTime < minBeginTime)
                {
                    minBeginTime = beginTime;
                }
                if (endTime > maxEndTime)
                {
                    maxEndTime = endTime;
                }
            }
            return new EntityInformation(entity, sumTotalDamage, minBeginTime, maxEndTime);
        }

        public Skills GetSkills(long beginTime, long endTime)
        {
            var sql =
                "SELECT amount, type, target, source, pet_zone, pet_id, skill_id, hotdot, critic, time, direction FROM skills WHERE time BETWEEN $begin AND $end ;";

            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$begin", beginTime);
            command.Parameters.AddWithValue("$end", endTime);

            var targetSourceSkills = new Dictionary<EntityId, Dictionary<EntityId, List<Skill>>>();
            var sourceTargetSkills = new Dictionary<EntityId, Dictionary<EntityId, List<Skill>>>();
            var sourceTargetIdSkill = new Dictionary<EntityId, Dictionary<EntityId, Dictionary<int, List<Skill>>>>();
            var sourceIdSkill = new Dictionary<EntityId, Dictionary<int, List<Skill>>>();
            var rdr = command.ExecuteReader();

            while (rdr.Read())
            {
                var amount = rdr.GetFieldValue<long>(rdr.GetOrdinal("amount"));
                var type = (Type) rdr.GetFieldValue<long>(rdr.GetOrdinal("type"));
                var target = new EntityId((ulong) rdr.GetFieldValue<long>(rdr.GetOrdinal("target")));
                var source = new EntityId((ulong) rdr.GetFieldValue<long>(rdr.GetOrdinal("source")));
                var skillid = rdr.GetFieldValue<long>(rdr.GetOrdinal("skill_id"));
                var direction =  (HitDirection)rdr.GetFieldValue<long>(rdr.GetOrdinal("direction"));
                var critic = rdr.GetFieldValue<long>(rdr.GetOrdinal("critic")) == 1;
                var hotdot = rdr.GetFieldValue<long>(rdr.GetOrdinal("hotdot")) == 1;
                var time = rdr.GetFieldValue<long>(rdr.GetOrdinal("time"));
                var petZone = rdr.IsDBNull(rdr.GetOrdinal("pet_zone"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("pet_zone"));
                var petId = rdr.IsDBNull(rdr.GetOrdinal("pet_id"))
                ? 0
                : rdr.GetFieldValue<long>(rdr.GetOrdinal("pet_id"));
                var pet = BasicTeraData.Instance.MonsterDatabase.GetOrNull((ushort)petZone, (uint)petId);
                var skill = new Skill(amount, type, target, source, (int) skillid, hotdot, critic, time, pet, direction);

                if (!targetSourceSkills.ContainsKey(skill.Target))
                {
                    targetSourceSkills.Add(skill.Target, new Dictionary<EntityId, List<Skill>>());
                }

                if (!targetSourceSkills[skill.Target].ContainsKey(skill.Source))
                {
                    targetSourceSkills[skill.Target].Add(skill.Source, new List<Skill>());
                }

                if (!sourceTargetSkills.ContainsKey(skill.Source))
                {
                    sourceTargetIdSkill.Add(skill.Source, new Dictionary<EntityId, Dictionary<int, List<Skill>>>());
                    sourceIdSkill.Add(skill.Source, new Dictionary<int, List<Skill>>());
                    sourceTargetSkills.Add(skill.Source, new Dictionary<EntityId, List<Skill>>());
                }

                if (!sourceTargetSkills[skill.Source].ContainsKey(skill.Target))
                {
                    sourceTargetSkills[skill.Source].Add(skill.Target, new List<Skill>());
                    sourceTargetIdSkill[skill.Source].Add(skill.Target, new Dictionary<int, List<Skill>>());
                }

                if (!sourceTargetIdSkill[skill.Source][skill.Target].ContainsKey(skill.SkillId))
                {
                    sourceTargetIdSkill[skill.Source][skill.Target].Add(skill.SkillId, new List<Skill>());
                }

                if (!sourceIdSkill[skill.Source].ContainsKey(skill.SkillId))
                {
                    sourceIdSkill[skill.Source].Add(skill.SkillId, new List<Skill>());
                }

                targetSourceSkills[skill.Target][skill.Source].Add(skill);
                sourceTargetSkills[skill.Source][skill.Target].Add(skill);
                sourceTargetIdSkill[skill.Source][skill.Target][skill.SkillId].Add(skill);
                sourceIdSkill[skill.Source][skill.SkillId].Add(skill);
            }

            var skills = new Skills(sourceTargetSkills, targetSourceSkills, sourceTargetIdSkill, sourceIdSkill);
            return skills;
        }


        public void PlayerEntityIdChange(EntityId oldId, EntityId newId)
        {
            var sql = "UPDATE skills SET source = $newId WHERE source = $oldId; ";
            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$newId", newId.Id);
            command.Parameters.AddWithValue("$oldId", oldId.Id);
            command.ExecuteNonQuery();

            sql = "UPDATE skills SET target = $newId WHERE target = $oldId; ";
            command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$newId", newId.Id);
            command.Parameters.AddWithValue("$oldId", oldId.Id);
            command.ExecuteNonQuery();
        }

        public List<PlayerHealDealt> PlayerHealInformation(long beginTime, long endTime)
        {
            var sql =
                "SELECT SUM(critic) as number_critics, COUNT(*) AS number_hits, source " +
                "FROM skills " +
                "WHERE time BETWEEN $begin AND $end AND type = $type " +
                "GROUP BY source ";

            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$begin", beginTime);
            command.Parameters.AddWithValue("$end", endTime);
            command.Parameters.AddWithValue("$type", Type.Heal);
            var result = new List<PlayerHealDealt>();

            var rdr = command.ExecuteReader();

            while (rdr.Read())
            {
                var source = new EntityId((ulong)rdr.GetInt64(rdr.GetOrdinal("source")));
                var entity = NetworkController.Instance.EntityTracker.GetOrPlaceholder(source);
                if (!(entity is UserEntity)) continue;
                var user = (UserEntity)entity;
                var player = NetworkController.Instance.PlayerTracker.GetOrNull(user.ServerId, user.PlayerId);
                if (player == null) continue;
                var critic = rdr.IsDBNull(rdr.GetOrdinal("number_critics"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("number_critics"));
                var hit = rdr.IsDBNull(rdr.GetOrdinal("number_hits"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("number_hits"));

                result.Add(new PlayerHealDealt(
                    critic,
                    hit,
                    player
                    ));
            }
            return result;

        }


        public List<PlayerDamageDealt> PlayerDamageInformation(long beginTime, long endTime)
        {
            var sql =
                "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, SUM(critic) as number_critics, COUNT(*) AS number_hits, source, target " +
                "FROM skills " +
                "WHERE time BETWEEN $begin AND $end AND type = $type " +
                "GROUP BY type, source " +
                "ORDER BY `total_amount` DESC;";

            var command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$begin", beginTime);
            command.Parameters.AddWithValue("$type", Type.Damage);
            command.Parameters.AddWithValue("$end", endTime);

            return PlayerDamageInformation(command);
        }

        private static List<PlayerDamageDealt> PlayerDamageInformation(SQLiteCommand command)
        {
            var result = new List<PlayerDamageDealt>();

            var rdr = command.ExecuteReader();

            while (rdr.Read())
            {
                var source = new EntityId((ulong) rdr.GetInt64(rdr.GetOrdinal("source")));
                var entity = NetworkController.Instance.EntityTracker.GetOrPlaceholder(source);
                if (!(entity is UserEntity)) continue;
                var user = (UserEntity) entity;
                var player = NetworkController.Instance.PlayerTracker.GetOrNull(user.ServerId, user.PlayerId);
                if (player == null) continue;
                var amount = rdr.IsDBNull(rdr.GetOrdinal("total_amount"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("total_amount"));
                var beginTime = rdr.IsDBNull(rdr.GetOrdinal("start_time"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("start_time"));
                var endTime = rdr.IsDBNull(rdr.GetOrdinal("end_time"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("end_time"));
                var critic = rdr.IsDBNull(rdr.GetOrdinal("number_critics"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("number_critics"));
                var hit = rdr.IsDBNull(rdr.GetOrdinal("number_hits"))
                    ? 0
                    : rdr.GetFieldValue<long>(rdr.GetOrdinal("number_hits"));
                var entityId = rdr.GetFieldValue<long>(rdr.GetOrdinal("target"));

                result.Add(new PlayerDamageDealt(
                    amount,
                    beginTime,
                    endTime,
                    critic,
                    hit,
                    player,
                    new EntityId((ulong) entityId)
                    ));
            }
            return result;
        }

        public List<PlayerDamageDealt> PlayerDamageInformation(NpcEntity target)
        {
            SQLiteCommand command;
            string sql;
            if (target == null)
            {
                sql =
                    "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, SUM(critic) as number_critics, COUNT(*) AS number_hits, source, target " +
                    "FROM skills WHERE type = $type GROUP BY source ORDER BY `total_amount` DESC;";
                command = new SQLiteCommand(sql, Connexion);
                command.Parameters.AddWithValue("$type", Type.Damage);
                return PlayerDamageInformation(command);
            }

            sql =
                "SELECT SUM(amount) as total_amount, MIN(time) as start_time, MAX(time) as end_time, SUM(critic) as number_critics, COUNT(*) AS number_hits, source, target " +
                "FROM skills " +
                "WHERE target = $target AND type = $type " +
                "GROUP BY source " +
                "ORDER BY `total_amount`  DESC;";
            command = new SQLiteCommand(sql, Connexion);
            command.Parameters.AddWithValue("$target", target.Id.Id);
            command.Parameters.AddWithValue("$type", Type.Damage);

            return PlayerDamageInformation(command);
        }

    }
}