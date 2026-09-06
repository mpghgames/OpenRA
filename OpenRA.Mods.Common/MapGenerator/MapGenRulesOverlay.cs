#region Copyright & License Information
/*
 * Copyright (c) The OpenRA Developers and Contributors
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using OpenRA.FileSystem;

namespace OpenRA.Mods.Common.MapGenerator
{
	// Rules overlays for generated maps. Each overlay is a folder or a zip file
	// in the per-mod map-rule-overlays support dir: its files are copied into
	// the generated map package, and the top-level blocks of its _extension.yaml
	// replace the corresponding map.yaml blocks.
	static class MapGenRulesOverlay
	{
		static string OverlaysPath(ModData modData) =>
			Platform.ResolvePath($"^SupportDir|map-rule-overlays/{modData.Manifest.Id}/{modData.Manifest.Metadata.Version}");

		public static IReadOnlyList<string> AvailableOverlays(ModData modData)
		{
			var names = new SortedSet<string>();
			var overlayDir = new Folder(OverlaysPath(modData));
			foreach (var entry in overlayDir.Contents)
			{
				using var overlayPackage = overlayDir.OpenPackage(entry, modData.ModFiles);
				if (overlayPackage != null)
					names.Add(entry);
			}

			return names.ToArray();
		}

		public static bool IsPresent(ModData modData, string overlay)
		{
			if (string.IsNullOrEmpty(overlay))
				return false;

			// The name is user-selectable, so guard against path traversal.
			if (overlay.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || overlay is "." or "..")
				return false;

			var overlayDir = new Folder(OverlaysPath(modData));
			using var overlayPackage = overlayDir.OpenPackage(overlay, modData.ModFiles);
			return overlayPackage != null;
		}

		// Applies the overlay to a freshly saved map package; a missing overlay
		// is logged and generation proceeds without it.
		public static void Apply(IReadWritePackage package, string overlay, ModData modData)
		{
			var overlayDir = new Folder(OverlaysPath(modData));
			using var overlayPackage = overlayDir.OpenPackage(overlay, modData.ModFiles);
			if (overlayPackage == null)
			{
				Log.Write("mapgen", $"Map gen rules overlay '{overlay}' not found, generating without it");
				return;
			}

			foreach (var file in overlayPackage.Contents)
				package.Update(file, overlayPackage.GetStream(file).ReadAllBytes());

			var extensionNodes = new List<MiniYamlNode>();
			var extensionStream = overlayPackage.GetStream("_extension.yaml");
			if (extensionStream != null)
			{
				using (extensionStream)
					extensionNodes = MiniYaml.FromStream(extensionStream, "_extension.yaml")
						.Where(node => node.Key.Length > 0)
						.ToList();
			}

			if (extensionNodes.Count == 0)
				return;

			using var mapStream = package.GetStream("map.yaml");
			var mapYaml = MiniYaml.FromStream(mapStream, "map.yaml").ToList();
			foreach (var extNode in extensionNodes)
			{
				var index = mapYaml.FindIndex(n => n.Key == extNode.Key);
				if (index >= 0)
					mapYaml[index] = extNode;
				else
				{
					// map.yaml is expected to have empty lines between top-level blocks
					mapYaml.Add(new MiniYamlNode("", ""));
					mapYaml.Add(extNode);
				}
			}

			package.Update("map.yaml", Encoding.UTF8.GetBytes(mapYaml.WriteToString()));
		}
	}
}
