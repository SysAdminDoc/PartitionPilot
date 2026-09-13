# README hero audit

## Evidence reviewed

The production WPF application was captured on a private Windows desktop on 2026-09-12. Simulation mode supplied realistic disks without reading or changing a physical drive. The four pre-edit captures are 1440 by 863 pixels and match the previously published screenshots byte for byte. A second private-desktop run refreshed the same product states for the release.

The live GitHub README was also reviewed before editing. It opened with a small centered icon and repeated the partition workspace farther down the page. The existing social card was not used in the README and included a release number inside its product screenshot.

## Findings

| Priority | Finding | Decision |
| --- | --- | --- |
| High | The README had no marketing hero. The strongest existing card was stored only as a social image. | Add one hero as the first README content. |
| High | The old card showed a release number in the embedded interface. It would become stale on the next release. | Preserve it as source evidence and build the new hero from a version-free crop. |
| Medium | Repeating the full partition screenshot below a hero would show the same view twice. | Remove that later screenshot while keeping the three different product views. |
| Medium | The opening identity was polished but too small to explain the product at a glance. | Keep the approved logo and pair it with the product promise plus real interface evidence. |

## Candidate review

Candidate 01 preserves the earlier “See the plan” message. It is clear and visually balanced, but “Then touch the disk” sounds less controlled than the product's actual safety model.

Candidate 02 makes the safety promise specific: know what changes, review first, and apply when ready. Its supporting labels cover queued work, identity checks, recovery evidence, and the included CLI. This direction is easier to trust and better matches the product.

Candidate 02 was inspected at 1280, 960, and 640 pixels. The logo stays distinct, the headline remains readable, no copy is clipped, and the real interface still reads as product evidence. The approved storage-route identity was not changed.

## Publication result

The selected hero is 1280 by 640 pixels in RGB format. The README references it once as its first content. The repeated partition workspace image was removed from the body, and the source, rejected layout, comparison sheets, final selection, both capture runs, and README before-and-after copies remain in this folder.
