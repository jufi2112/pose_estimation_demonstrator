import socket
import time
from datetime import datetime
from os import path as osp
import os
import numpy as np
import quaternion
import argparse
import selectors
import types
import sys

class BodyPoseTcpClient:
    """Loosely based on this article: https://realpython.com/python-sockets/"""
    def __init__(self, host, port, record, record_dir, bodies_to_record,
                 is_producer, angle, keep_yz_axes, npz_file = None,
                 poses_attribute = None, transl_attribute = None,
                 capture_fps_attribute = None, target_fps = -1,
                 capture_fps = -1, drop_frames = False, loop = True,
                 verbosity = 1):
        self.host = host
        self.port = port
        self.is_producer = is_producer
        # to transform from given coordinate system to unity
        self.x_rot_angle = angle
        self.keep_yz_axes = keep_yz_axes
        self.record = record
        self.record_dir = record_dir if record_dir != None else os.getcwd()
        self.bodies_to_record = bodies_to_record
        self.poses = None
        self.transl = None
        self.verbosity = verbosity
        self.recordings = {}
        self.recording_time_start = -1
        self.time_last_frame = 0
        mocap_fps = None
        if self.is_producer and self.record:
            raise ValueError(
                "The client cannot record and be a producer at the same time!"
            )
        if self.is_producer and npz_file:
            self.poses, self.transl, mocap_fps = self._load_npz_attributes(
                npz_file,
                poses_attribute,
                transl_attribute,
                capture_fps_attribute
            )
            # Transform poses and translation to SMPL-X's coordinate system
            if self.poses is not None:
                self._add_x_angle_offset_to_poses()
            if self.transl is not None and not self.keep_yz_axes:
                self.transl = self._swap_translation_yz_axes(self.transl)
            if self.poses is None or self.transl is None:
                if self.verbosity > 0:
                    print("Defaulting to sending 1 and 0 poses.")
        self.transmit_ones = True
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
        self.target_fps = target_fps if target_fps != -1 else self.capture_fps
        if self.target_fps == 0:
            raise ValueError(f"Invalid target fps: {self.target_fps}")
        if self.is_producer:
            self.time_to_sleep = 1 / self.target_fps
        else:
            self.time_to_sleep = 0
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
        if self.record and self.verbosity > 0:
            print("Recording data")
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
            if self.record:
                self._save_recording()


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
            if self.verbosity > 1:
                print(f"Body index {body_id} | Translation: {transl} | "
                      f"Pose shape: {pose.shape} | First 6 elements: {pose[:6]}")
            if self.record:
                if self.bodies_to_record is None or body_id in self.bodies_to_record:
                    curr_time = time.perf_counter()
                    if body_id not in list(self.recordings.keys()):
                        self.recordings[body_id] = {
                            'poses': [pose],
                            'transl': [transl],
                            'time_first_frame': curr_time,
                            'time_last_frame': curr_time
                        }
                    else:
                        self.recordings[body_id]['poses'].append(pose)
                        self.recordings[body_id]['transl'].append(transl)
                        self.recordings[body_id]['time_last_frame'] = curr_time
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
                #print(f"Sleeping for {to_sleep} seconds")
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


    def _add_x_angle_offset_to_poses(self):
        """alternatively, see https://math.stackexchange.com/questions/382760/composition-of-two-axis-angle-rotations"""
        quats = quaternion.from_rotation_vector(self.poses[:, :3])
        rotX = quaternion.from_rotation_vector(np.asarray([1,0,0]) * np.deg2rad(self.x_rot_angle))
        self.poses[:, :3] = quaternion.as_rotation_vector(rotX * quats)


    def _save_recording(self):
        fname = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        fpath = osp.join(self.record_dir, fname) + '.npz'
        if self.verbosity > 0:
            print(f"Saving recordings to {fpath}...", end=' ')
        rec_dir = {}
        for body_id in list(self.recordings.keys()):
            rec_dir[f'{body_id}_poses'] = np.stack(self.recordings[body_id]['poses'])
            rec_dir[f'{body_id}_transl'] = np.stack(self.recordings[body_id]['transl'])
            rec_dir[f'{body_id}_mocap_frame_rate'] = int(
                rec_dir[f'{body_id}_poses'].shape[0] / (
                    self.recordings[body_id]['time_last_frame'] -
                    self.recordings[body_id]['time_first_frame'] + 1e-7
                )
            )
            del self.recordings[body_id]
        with open(fpath, 'wb') as file:
            np.savez(file, **rec_dir)
        if self.verbosity > 0:
            print('done')


    def _swap_translation_yz_axes(self, arr):
        if len(arr.shape) == 1:
            arr = arr[np.newaxis, ...]
        return arr[:, [0,2,1]]
        # Deprecated
        # swap_yz_mat = np.asarray(
        #             [
        #                 [1,0,0],
        #                 [0,0,1],
        #                 [0,1,0]
        #             ],
        #             dtype=np.float32
        #         )
        # # swap y and z Axis to conform to Unity's coordinate system
        # return np.einsum('ij,kj->ki', swap_yz_mat, arr)
        

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="TCP Client")
    parser.add_argument('--host', type=str, default="localhost",
                        help="<Optional> Host to connect to. Defaults to "
                        "localhost")
    parser.add_argument('-p', '--port', type=int, default=7777,
                        help="<Optional> Port to connect to. Defaults to 7777")
    parser.add_argument('--record', action='store_true', help="Defines that "
                        "this client should record all transmitted information."
                        " Does not work if the client is a producer.")
    parser.add_argument('-o', '--output', type=str, default=None,
                        help="<Optional> Directory to where the recording "
                        "should be saved to. Defaults to None (cwd)")
    parser.add_argument('-b', '--bodies-to-record', nargs='*', help="<Optional>"
                        " Body IDs to record. Defaults to recording all")
    parser.add_argument('--producer', action='store_true', help="Defines that "
                        "this client is going to be a producer")
    parser.add_argument('-d', '--data', type=str, default=None,
                        help="<Optional> Path to a .npz file that contains the "
                        "poses that should be broadcasted. Defaults to None")
    parser.add_argument('-a', '--angle', type=float, default=-90,
                        help="<Optional> Angle (in deg) by which the global "
                        "orientation should be rotated around the x-Axis in "
                        "order to match Unity's default rotation. Defaults to "
                        "-90°")
    parser.add_argument('--keep-yz-axes', action='store_true', help="<Optional>"
                        ". If specified, y and z Axes will not be swapped.")
    parser.add_argument('--poses-field', type=str, default="poses",
                        help="<Optional> Name of the npz field that contains "
                        "the poses to broadcast. Defaults to 'poses'.")
    parser.add_argument('--transl-field', type=str, default="trans",
                        help="<Optional> Name of the npz field that contains "
                        "the translations to broadcast. Defaults to 'trans'.")
    parser.add_argument('--mocap-fps-field', type=str, default="mocap_frame_rate",
                        help="<Optional> Name of the field that holds the "
                        "capture frame rate value. Defaults to 'mocap_frame_rate'")
    parser.add_argument('-f', '--fps', type=float, default=-1,
                        help="<Optional> FPS to broadcast the poses with "
                        "should the client act as a producer. Defaults to -1 "
                        "(= use mocap frame rate)")
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
    bodies_to_record = None
    if args.bodies_to_record is not None:
        bodies_to_record = [int(x) for x in args.bodies_to_record]

    client = BodyPoseTcpClient(args.host, args.port, args.record, args.output,
                               bodies_to_record, args.producer, args.angle,
                               args.keep_yz_axes, args.data, args.poses_field,
                               args.transl_field, args.mocap_fps_field,
                               args.fps, args.capture_fps, args.drop_frames,
                               not args.noloop, args.verbosity
    )
    client.connect()
    client.run()
