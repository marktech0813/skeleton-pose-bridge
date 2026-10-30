#include "pose_stream.h"

#include <stdlib.h>
#include <string.h>

#ifdef _WIN32
#define WIN32_LEAN_AND_MEAN
#include <winsock2.h>
#include <ws2tcpip.h>
typedef SOCKET pose_socket;
#define POSE_INVALID_SOCKET INVALID_SOCKET
#define pose_close closesocket
#else
#include <arpa/inet.h>
#include <netinet/in.h>
#include <sys/socket.h>
#include <unistd.h>
typedef int pose_socket;
#define POSE_INVALID_SOCKET (-1)
#define pose_close close
#endif

struct PoseStream {
    pose_socket socket;
    struct sockaddr_in dest;
};

static void write_u16(uint8_t* p, uint16_t v)
{
    p[0] = (uint8_t)(v & 0xffu);
    p[1] = (uint8_t)((v >> 8) & 0xffu);
}

static void write_u32(uint8_t* p, uint32_t v)
{
    p[0] = (uint8_t)(v & 0xffu);
    p[1] = (uint8_t)((v >> 8) & 0xffu);
    p[2] = (uint8_t)((v >> 16) & 0xffu);
    p[3] = (uint8_t)((v >> 24) & 0xffu);
}

static void write_f32(uint8_t* p, float v)
{
    uint32_t bits = 0;
    memcpy(&bits, &v, sizeof(bits));
    write_u32(p, bits);
}

PoseStream* pose_stream_open(const char* host, int port)
{
#ifdef _WIN32
    WSADATA data;
    if (WSAStartup(MAKEWORD(2, 2), &data) != 0)
        return NULL;
#endif
    if (host == NULL || port <= 0 || port > 65535)
        return NULL;

    PoseStream* stream = (PoseStream*)calloc(1, sizeof(PoseStream));
    if (stream == NULL)
        return NULL;

    stream->socket = (pose_socket)socket(AF_INET, SOCK_DGRAM, 0);
    if (stream->socket == POSE_INVALID_SOCKET) {
        free(stream);
        return NULL;
    }

    memset(&stream->dest, 0, sizeof(stream->dest));
    stream->dest.sin_family = AF_INET;
    stream->dest.sin_port = htons((uint16_t)port);
    if (inet_pton(AF_INET, host, &stream->dest.sin_addr) != 1) {
        pose_close(stream->socket);
        free(stream);
        return NULL;
    }
    return stream;
}

int pose_stream_send(PoseStream* stream, const PoseJoint* joints, int joint_count,
                     uint32_t timestamp_ms, uint16_t flags)
{
    uint8_t packet[16 + 32 * 16];
    int i;
    if (stream == NULL || joints == NULL)
        return -1;
    if (joint_count != 17 && joint_count != 25)
        return -1;
    if ((size_t)(16 + joint_count * 16) > sizeof(packet))
        return -1;

    packet[0] = 'P';
    packet[1] = '3';
    packet[2] = 'D';
    packet[3] = '1';
    write_u16(packet + 4, POSE_VERSION);
    write_u16(packet + 6, flags);
    write_u32(packet + 8, timestamp_ms);
    write_u16(packet + 12, (uint16_t)joint_count);
    write_u16(packet + 14, 0);

    for (i = 0; i < joint_count; i++) {
        uint8_t* joint = packet + 16 + i * 16;
        write_f32(joint, joints[i].x);
        write_f32(joint + 4, joints[i].y);
        write_f32(joint + 8, joints[i].z);
        write_f32(joint + 12, joints[i].confidence);
    }

    {
        int bytes = 16 + joint_count * 16;
        int sent = (int)sendto(stream->socket, (const char*)packet, (size_t)bytes, 0,
                               (const struct sockaddr*)&stream->dest, sizeof(stream->dest));
        return sent == bytes ? 0 : -1;
    }
}

void pose_stream_close(PoseStream* stream)
{
    if (stream == NULL)
        return;
    if (stream->socket != POSE_INVALID_SOCKET)
        pose_close(stream->socket);
    free(stream);
#ifdef _WIN32
    WSACleanup();
#endif
}
