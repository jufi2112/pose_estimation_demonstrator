import socket
import time
from os import path as osp
import os
import numpy as np
import argparse
import selectors
import types

class BodyPoseTcpClient:
    """Loosely based on this article: https://realpython.com/python-sockets/"""
    def __init__(self, host, port, is_producer, npz_file = None,
                 poses_attribute = None, transl_attribute = None,
                 capture_fps_attribute = None, target_fps = -1,
                 capture_fps = -1, drop_frames = False, loop = True,
                 verbosity = 1):
        self.host = host
        self.port = port
        self.is_producer = is_producer
        self.poses = None
        self.transl = None
        self.verbosity = verbosity
        mocap_fps = None
        if self.is_producer and npz_file:
            self.poses, self.transl, mocap_fps = self._load_npz_attributes(
                npz_file,
                poses_attribute,
                transl_attribute,
                capture_fps_attribute
            )
            if self.poses is None or self.transl is None:
                if self.verbosity > 0:
                    print("Defaulting to sending 1 and 0 poses.")
        self.transmit_ones = True
        self.target_fps = target_fps
        if self.target_fps == 0:
            raise ValueError(f"Invalid target fps: {self.target_fps}")
        self.time_to_sleep = 1 / self.target_fps
        self.capture_fps = mocap_fps if capture_fps == -1 else capture_fps
        if drop_frames and self.capture_fps is None:
            raise ValueError(
                "Could not infer motion capture fps from .npz file via "
                f" the {capture_fps_attribute} attribute. Unable to determine "
                "number of frames that have to be dropped to match target "
                f"frame rate of {target_fps} fps. Try providing the capture "
                "fps via the --capture-fps option."
            )
        if drop_frames and self.capture_fps < 1:
            raise ValueError(f"Invalid capture fps: {self.capture_fps}")
        self.frames_to_advance = int(self.capture_fps / self.target_fps) \
            if drop_frames else 1
        self.loop = loop
        self.sock = None
        self.sel = None


    def connect(self):
        self.sel = selectors.DefaultSelector()
        server_addr = (self.host, self.port)
        self.sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.sock.setblocking(False)
        if self.verbosity > 0:
            print(f'Connecting to server at {server_addr} ...', end=" ")
        self.sock.connect_ex(server_addr)
        events = selectors.EVENT_READ | selectors.EVENT_WRITE
        data = None
        self.sel.register(self.sock, events, data=data)
        if self.verbosity > 0:
            print("done")


    def run(self):
        if self.is_producer and self.verbosity > 0:
            print("Transmitting data")
        try:
            continue_connection = True
            poses_idx = 0
            time_last_transmission = time.perf_counter()
            while continue_connection:
                data_to_transmit = None
                if isinstance(self.poses, np.ndarray) and isinstance(self.transl, np.ndarray):
                    if poses_idx >= self.poses.shape[0]:
                        if not self.loop:
                            if self.verbosity > 0:
                                print(
                                    "Transmitted all poses, closing connection"
                                )
                            break
                        poses_idx = 0
                    current_pose = self.poses[poses_idx]
                    current_transl = self.transl[poses_idx]
                    data_to_transmit = np.concatenate(
                        (current_transl, current_pose),
                        axis=None
                    )
                events = self.sel.select(timeout=None)
                for key, mask in events:
                    status = self.service_connection(key, mask,
                                                     data_to_transmit,
                                                     time_last_transmission
                    )
                    if not status:
                        continue_connection = False
                        break
                    if isinstance(status, float):
                        time_last_transmission = status
                poses_idx += self.frames_to_advance
        except KeyboardInterrupt:
            if self.verbosity > 0:
                print("Closing connection")
        finally:
            self.sel.unregister(self.sock)
            self.sock.close()
            self.sel.close()


    def service_connection(self, key, mask, data_to_transmit,
                           time_last_transmission) -> bool:
        if mask & selectors.EVENT_READ:
            recv_data = self.sock.recv(676)
            if not recv_data:
                if self.verbosity > 0:
                    print("Server closed the connection")
                return False
            if self.is_producer:
                return True
            body_id = int.from_bytes(recv_data[:4], 'big')
            transl = np.frombuffer(recv_data[4:16], dtype=np.float32)
            pose = np.frombuffer(recv_data[16:], dtype=np.float32)
            if self.verbosity > 0:
                print(f"Body index {body_id} | Translation: {transl} | "
                      f"Pose shape: {pose.shape} | First 6 elements: {pose[:6]}")
            return True
        if mask & selectors.EVENT_WRITE:
            if not self.is_producer:
                return True
            to_transmit = data_to_transmit
            if not isinstance(to_transmit, np.ndarray):
                to_transmit = np.zeros((168,), dtype=np.float32)
                if self.transmit_ones:
                    to_transmit += 1
                self.transmit_ones = not self.transmit_ones
            to_sleep = self.time_to_sleep - (time.perf_counter() - time_last_transmission)
            if to_sleep > 0:
                time.sleep(to_sleep)
            if self.verbosity > 1:
                print(f"Sending {to_transmit.shape} of size {len(to_transmit.tobytes())}")
            self.sock.sendall(to_transmit.tobytes())
            return time.perf_counter()


    def _load_npz_attributes(self, npz_file, pose_attribute, transl_attribute,
                             capture_fps_attribute):
        if not osp.isfile(npz_file):
            return None, None, None
        content = np.load(npz_file)
        if not pose_attribute in list(content.keys()):
            return None, None, None
        if not transl_attribute in list(content.keys()):
            return None, None, None
        return (
            content[pose_attribute].astype(np.float32),
            content[transl_attribute].astype(np.float32),
            content.get(capture_fps_attribute, default=None)
        )


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="TCP Client")
    parser.add_argument('--host', type=str, default="localhost",
                        help="<Optional> Host to connect to. Defaults to "
                        "localhost")
    parser.add_argument('-p', '--port', type=int, default=7777,
                        help="<Optional> Port to connect to. Defaults to 7777")
    parser.add_argument('--producer', action='store_true', help="Defines that "
                        "this client is going to be a producer")
    parser.add_argument('-d', '--data', type=str, default=None,
                        help="<Optional> Path to a .npz file that contains the "
                        "poses that should be broadcasted. Defaults to None")
    parser.add_argument('--poses-field', type=str, default="poses",
                        help="<Optional> Name of the npz field that contains "
                        "the poses to broadcast. Defaults to 'poses'.")
    parser.add_argument('--transl-field', type=str, default="trans",
                        help="<Optional> Name of the npz field that contains "
                        "the translations to broadcast. Defaults to 'trans'.")
    parser.add_argument('--mocap-fps-field', type=str, default="mocap_frame_rate",
                        help="<Optional> Name of the field that holds the "
                        "capture frame rate value. Defaults to 'mocap_frame_rate'")
    parser.add_argument('-f', '--fps', type=float, default=120,
                        help="<Optional> FPS to broadcast the poses with "
                        "should the client act as a producer. Defaults to 120")
    parser.add_argument('--capture-fps', type=int, default=-1,
                        help="<Optional> Framerate with which the data was "
                        "captured. This will overwrite the value provided by "
                        "the --mocap-fps-field information. Defaults to -1 "
                        "(inactive).")
    parser.add_argument('--drop-frames', action="store_true",
                        help="If the target replay framerate (--fps option) "
                        "does not match the capture framerate, this flag "
                        "specifies that frames should be dropped in order to "
                        "match the target framerate. Requires that the "
                        "capturing framerate is known.")
    parser.add_argument('--noloop', action='store_true', help="If specified, "
                        "the poses will only be broadcasted once.")
    parser.add_argument('-v', '--verbosity', type=int, default=1,
                        help="<Optional> Verbosity setting. Defaults to 1")
    args = parser.parse_args()

    client = BodyPoseTcpClient(args.host, args.port, args.producer, args.data,
                               args.poses_field, args.transl_field,
                               args.mocap_fps_field, args.fps,
                               args.capture_fps, args.drop_frames,
                               not args.noloop, args.verbosity
    )
    client.connect()
    client.run()
