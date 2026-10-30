CC ?= gcc
CFLAGS ?= -O2 -Wall -Wextra -Werror -Iinclude
LDFLAGS ?= -lm

.PHONY: all clean test

all: demo_walk_sender

demo_walk_sender: src/demo_walk_sender.c src/pose_stream.c include/pose_stream.h
	$(CC) $(CFLAGS) -o demo_walk_sender src/demo_walk_sender.c src/pose_stream.c $(LDFLAGS)

clean:
	rm -f demo_walk_sender

test: demo_walk_sender
	python3 test_packet.py
