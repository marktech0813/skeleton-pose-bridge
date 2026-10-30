# Skeleton Pose Bridge

Unity plays a person from a 3D skeleton. Press Play and a figure walks immediately. Point the C detector at UDP port **5055** and the figure switches to that person.

The project is the folder that contains `Assets` and `ProjectSettings`. Open it in Unity Hub with **2022.3 LTS** or newer. The scene is `Assets/Scenes/PoseStage.unity`.

## Why the model was being destroyed

Copying each joint onto a bone box breaks the rig in three ways:

- A parent box scaled to the bone length multiplies every child. The next frame compounds that scale until the mesh is flat or huge.
- Writing `localPosition` on a skinned bone leaves the bind pose, so the mesh tears.
- A zero-length bone fed to `LookRotation` becomes a NaN rotation. The renderer then drops the object, which looks like it was destroyed.

Colliders and rigidbodies on those boxes do the same thing through physics.

This project never does that. Bone pivots stay at scale 1 and keep the local positions captured at the start of Play. Only rotations change, plus the hips translation. Capsule and box meshes are leaves, so their size cannot scale the next joint. Incoming values that are not finite are dropped and the last good pose is held. Rigidbodies under a driven model are set to kinematic.

## What you see on Play

With no network data, a built-in walk plays, facing the camera. The overlay shows the source. Keys:

| Key | Action |
| --- | --- |
| C | Recenter the figure on the current hips |
| D | Toggle the built-in walk and the live stream |
| L | Swap left and right |
| Y | Turn the pose 180 degrees |
| M | Show joint markers (separate from the bones) |
| V | Show or hide the built-in figure |
| G | Hide the overlay |

Markers are not parented to bones. They cannot scale the body.

## Your own character

1. Import the model. In the importer, set **Rig** to **Humanoid** and click Apply.
2. Drag the character into `PoseStage`.
3. Drag its **Animator** onto **Humanoid Animator** on the Pose Stage object.
4. Press Play. The built-in figure hides, and the character follows the skeleton.

The character should be standing in its reference pose in the scene before you press Play. Animator playback is turned off while the stream drives it, so an idle clip cannot fight the skeleton. The mesh object is not destroyed or reparented.

If the character faces away from you, press **Y** or set **Extra Yaw** to 180. If the arms are crossed, enable **Swap Left Right**. COCO "left" is the person's left, which is on the right side of the image when they face the camera.

## A box skeleton instead of a Humanoid

Add elements to **Generic Bones**. One link is **Is Root** (the hips). Parent the next joint under the previous one, and keep the visual mesh on a child that has no children of its own. Set **From** and **To** to the COCO joints that bone spans, for example Left Shoulder to Left Elbow.

Do not scale a transform that has children. The driver puts child positions and scales back to the bind values every frame, then writes the rotation.

## Detector protocol

`Native/include/pose_stream.h` is the packet the Unity side reads. Joint order is COCO-17. Camera space is x right, y down, z forward. Meters are the default. Set `POSE_FLAG_MILLIMETERS` if the GAN output is in millimeters. Unity rescales the body onto the figure, so pixel-sized or normalized coordinates still work as long as both hips and both shoulders are present.

From the detection app, after each frame:

```c
#include "pose_stream.h"

/* joints[POSE_LEFT_SHOULDER].x = x;
   joints[POSE_LEFT_SHOULDER].y = y;
   joints[POSE_LEFT_SHOULDER].z = z;
   joints[POSE_LEFT_SHOULDER].confidence = score;  // 0 if missing
*/
pose_stream_send(stream, joints, 17, timestamp_ms, POSE_FLAG_CAMERA_Y_DOWN);
```

Open the socket once:

```c
PoseStream* stream = pose_stream_open("127.0.0.1", 5055);
```

A 25-joint OpenPose BODY_25 packet is remapped to COCO. JSON is also accepted, with the same flags:

```json
{"t":1000,"flags":1,"joints":[x,y,z,confidence]}
```

`flags` 1 means camera Y-down. Add 2 for millimeters. There are 68 numbers for COCO (17 joints times x, y, z, confidence).

Build the sample sender and run it while Unity is playing. The right hand raises once every few seconds, which is how you can see that the live packet has replaced the built-in walk.

```bash
cd Native
make
./demo_walk_sender --host 127.0.0.1 --port 5055
```

`make test` checks the packet layout.

If the port is busy, change **Listen Port** on Pose Stage and pass the same `--port`. The port is read when Play starts.
