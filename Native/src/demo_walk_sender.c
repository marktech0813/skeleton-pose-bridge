#include "pose_stream.h"

#include <math.h>
#include <signal.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#include <windows.h>
#else
#include <time.h>
#endif

static volatile sig_atomic_t g_stop = 0;

static void on_signal(int signo)
{
    (void)signo;
    g_stop = 1;
}

static void put_unity(PoseJoint* joint, float x, float y, float z)
{
    /* Store the Unity Y-up point as camera Y-down. */
    joint->x = x;
    joint->y = -y;
    joint->z = z;
    joint->confidence = 1.0f;
}

static void fill_walk(PoseJoint joints[POSE_JOINT_COUNT], float time_s)
{
    float w = time_s * 4.4f;
    float s = sinf(w);
    float bob = 0.018f * cosf(w * 2.0f);
    float z = 0.42f * sinf(time_s * 0.55f);
    float hip_y = 0.92f + bob;
    float wave = 0.0f;
    float phase = fmodf(time_s, 4.0f);
    float lx, ly, lz;
    float rx, ry, rz;
    int i;

    for (i = 0; i < POSE_JOINT_COUNT; i++) {
        joints[i].x = 0.0f;
        joints[i].y = 0.0f;
        joints[i].z = 0.0f;
        joints[i].confidence = 1.0f;
    }

    /* A raised right hand, once per cycle, so a live stream is obvious. */
    if (phase > 2.6f && phase < 3.7f) {
        float u = sinf((phase - 2.6f) / 1.1f * 3.14159265f);
        wave = u > 0.0f ? u : 0.0f;
    }

    put_unity(&joints[POSE_NOSE], 0.015f * s, 1.62f + bob, z - 0.09f);
    put_unity(&joints[POSE_LEFT_EYE], 0.035f, 1.67f + bob, z - 0.06f);
    put_unity(&joints[POSE_RIGHT_EYE], -0.035f, 1.67f + bob, z - 0.06f);
    put_unity(&joints[POSE_LEFT_EAR], 0.09f, 1.65f + bob, z + 0.02f);
    put_unity(&joints[POSE_RIGHT_EAR], -0.09f, 1.65f + bob, z + 0.02f);
    put_unity(&joints[POSE_LEFT_SHOULDER], 0.20f, 1.38f + bob, z);
    put_unity(&joints[POSE_RIGHT_SHOULDER], -0.20f, 1.38f + bob, z);
    put_unity(&joints[POSE_LEFT_HIP], 0.11f + 0.02f * s, hip_y, z);
    put_unity(&joints[POSE_RIGHT_HIP], -0.11f + 0.02f * s, hip_y, z);

    lx = 0.11f + 0.02f * s;
    lz = z - 0.26f * s;
    ly = 0.07f + 0.06f * fabsf(s);
    put_unity(&joints[POSE_LEFT_ANKLE], lx, ly, lz);
    put_unity(&joints[POSE_LEFT_KNEE], lx, 0.50f, (z + lz) * 0.5f - 0.07f);

    rx = -0.11f + 0.02f * s;
    rz = z + 0.26f * s;
    ry = 0.07f + 0.06f * fabsf(s);
    put_unity(&joints[POSE_RIGHT_ANKLE], rx, ry, rz);
    put_unity(&joints[POSE_RIGHT_KNEE], rx, 0.50f, (z + rz) * 0.5f - 0.07f);

    put_unity(&joints[POSE_LEFT_WRIST], 0.24f, 0.78f, z + 0.30f * s);
    put_unity(&joints[POSE_LEFT_ELBOW], 0.30f, 1.10f, z + 0.15f * s + 0.07f);

    if (wave > 0.05f) {
        put_unity(&joints[POSE_RIGHT_WRIST], -0.12f, 1.38f + 0.45f * wave, z - 0.25f);
        put_unity(&joints[POSE_RIGHT_ELBOW], -0.28f, 1.35f + 0.12f * wave, z - 0.05f);
    } else {
        put_unity(&joints[POSE_RIGHT_WRIST], -0.24f, 0.78f, z - 0.30f * s);
        put_unity(&joints[POSE_RIGHT_ELBOW], -0.30f, 1.10f, z - 0.15f * s + 0.07f);
    }
}

static void sleep_ms(int ms)
{
#ifdef _WIN32
    Sleep((DWORD)ms);
#else
    struct timespec ts;
    ts.tv_sec = ms / 1000;
    ts.tv_nsec = (long)(ms % 1000) * 1000000L;
    nanosleep(&ts, NULL);
#endif
}

int main(int argc, char** argv)
{
    const char* host = "127.0.0.1";
    int port = 5055;
    int hz = 30;
    int count = 0;
    int sent = 0;
    int i;
    PoseStream* stream;

    for (i = 1; i < argc; i++) {
        if (strcmp(argv[i], "--host") == 0 && i + 1 < argc)
            host = argv[++i];
        else if (strcmp(argv[i], "--port") == 0 && i + 1 < argc)
            port = atoi(argv[++i]);
        else if (strcmp(argv[i], "--hz") == 0 && i + 1 < argc)
            hz = atoi(argv[++i]);
        else if (strcmp(argv[i], "--count") == 0 && i + 1 < argc)
            count = atoi(argv[++i]);
        else {
            fprintf(stderr, "Usage: %s [--host 127.0.0.1] [--port 5055] [--hz 30] [--count N]\n", argv[0]);
            return 2;
        }
    }
    if (hz < 1)
        hz = 1;

    signal(SIGINT, on_signal);
#ifdef SIGTERM
    signal(SIGTERM, on_signal);
#endif

    stream = pose_stream_open(host, port);
    if (stream == NULL) {
        fprintf(stderr, "Could not open UDP socket to %s:%d\n", host, port);
        return 1;
    }

    printf("Sending COCO-17 camera-space poses to %s:%d at %d Hz\n", host, port, hz);
    printf("Replace fill_walk() with your YOLO / GAN joints. Ctrl+C stops.\n");

    while (!g_stop && (count <= 0 || sent < count)) {
        PoseJoint joints[POSE_JOINT_COUNT];
        float time_s = (float)sent / (float)hz;
        fill_walk(joints, time_s);
        if (pose_stream_send(stream, joints, POSE_JOINT_COUNT,
                             (uint32_t)(time_s * 1000.0f), POSE_FLAG_CAMERA_Y_DOWN) != 0) {
            fprintf(stderr, "send failed\n");
            pose_stream_close(stream);
            return 1;
        }
        sent++;
        if (count <= 0 || sent < count)
            sleep_ms(1000 / hz);
    }

    pose_stream_close(stream);
    return 0;
}
