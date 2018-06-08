﻿using SS14.Client.Graphics;
using SS14.Client.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SS14.Client
{
    public class SceneTreeHolder : ISceneTreeHolder
    {
        public Godot.SceneTree SceneTree { get; private set; }

        public Godot.Node WorldRoot { get; private set; }

        public void Initialize(Godot.SceneTree tree)
        {
            SceneTree = tree ?? throw new ArgumentNullException(nameof(tree));

            if(true) //TODO 2DVS3D
            {
                WorldRoot = new Godot.Node2D
                {
                    Name = "WorldRoot"
                };
            }
            else
            {
                WorldRoot = new Godot.Spatial
                {
                    Name = "WorldRoot"
                };
            }
            SceneTree.GetRoot().AddChild(WorldRoot);
        }
    }
}
