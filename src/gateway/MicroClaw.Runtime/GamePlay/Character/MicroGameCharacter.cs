using System;
using System.Collections.Generic;
using System.Text;

namespace MicroClaw.Runtime.GamePlay;

public abstract class MicroGameCharacter : MicroGameObject
{
    protected MicroGameCharacter(string id, string worldId, string name)
       : base(id, worldId)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("角色必须有名字。", nameof(name));
        Name = name;
        Status = MicroGameCharacterStatus.Active;
        World = new MicroGameWorld(worldId, name);
    }

    public MicroGameWorld World { get; set; }


    public MicroGameCharacterStatus Status { get; protected set; }

    public override string Name
    {
        get => base.Name;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("江湖名不能为空。", nameof(value));
            base.Name = value;
        }
    }
}

