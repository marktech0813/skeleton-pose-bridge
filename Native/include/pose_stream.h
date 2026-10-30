#ifndef POSE_STREAM_H
#define POSE_STREAM_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * COCO-17 3D pose packet. "Left" is the person's left, not the left edge
 * of the image. Camera space is x right, y down, z forward, in meters.
 * Set POSE_FLAG_MILLIMETERS if the values are millimeters.
 * Set no camera flag if the values are already Unity space (x right, y up, z forward).
 *
 *   offset  size  field
 *   0       4     magic 'P' '3' 'D' '1'
 *   4       2     version = 1
 *   6       2     flags
 *   8       4     timestamp milliseconds
 *   12      2     joint count (17 or 25)
 *   14      2     reserved
 *   16      16*n  x, y, z, confidence as little-endian float32
 *
 * OpenPose BODY_25 (count 25) is remapped to COCO inside Unity.
 */

#define POSE_JOINT_COUNT 17
#define POSE_VERSION 1
#define POSE_FLAG_CAMERA_Y_DOWN 1u
#define POSE_FLAG_MILLIMETERS 2u

typedef enum PoseJointId {
    POSE_NOSE = 0,
    POSE_LEFT_EYE = 1,
    POSE_RIGHT_EYE = 2,
    POSE_LEFT_EAR = 3,
    POSE_RIGHT_EAR = 4,
    POSE_LEFT_SHOULDER = 5,
    POSE_RIGHT_SHOULDER = 6,
    POSE_LEFT_ELBOW = 7,
    POSE_RIGHT_ELBOW = 8,
    POSE_LEFT_WRIST = 9,
    POSE_RIGHT_WRIST = 10,
    POSE_LEFT_HIP = 11,
    POSE_RIGHT_HIP = 12,
    POSE_LEFT_KNEE = 13,
    POSE_RIGHT_KNEE = 14,
    POSE_LEFT_ANKLE = 15,
    POSE_RIGHT_ANKLE = 16
} PoseJointId;

typedef struct PoseJoint {
    float x;
    float y;
    float z;
    float confidence;
} PoseJoint;

typedef struct PoseStream PoseStream;

/* host is a dotted IPv4 address. Returns NULL on failure. */
PoseStream* pose_stream_open(const char* host, int port);

/* Returns 0 on success, -1 on failure. joints must hold joint_count entries. */
int pose_stream_send(PoseStream* stream, const PoseJoint* joints, int joint_count,
                     uint32_t timestamp_ms, uint16_t flags);

void pose_stream_close(PoseStream* stream);

#ifdef __cplusplus
}
#endif

#endif
