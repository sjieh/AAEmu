using AAEmu.Commons.Network;
using AAEmu.Game.Core.Network.Game;
using NLog;

namespace AAEmu.Game.Core.Packets.C2G;

public class CSLearnSkillPacket() : GamePacket(CSOffsets.CSLearnSkillPacket, 1)
{
    private static readonly Logger Logger = LogManager.GetCurrentClassLogger();

    public override void Read(PacketStream stream)
    {
        var skillId = stream.ReadUInt32();

        var character = Connection.ActiveChar;
        if (character == null)
        {
            Logger.Warn("LearnSkill rejected: skill={0} without an active character", skillId);
            return;
        }

        var accepted = character.Skills.AddSkill(skillId);
        if (!accepted)
            Logger.Info("LearnSkill request refused: character={0} skill={1}", character.Name, skillId);
    }
}
