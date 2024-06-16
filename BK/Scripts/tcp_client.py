import os
import time
import socket
import argparse
import selectors
import quaternion
import numpy as np

from dotmap import DotMap
from os import path as osp
from datetime import datetime
from typing import Union, List, Optional

try:
    import smpl_conversion
    smpl_conversion_imported = True
except ImportError:
    smpl_conversion_imported = False


class BodyPoseTcpClient:
    """
        TCP Client that sends SMPL-X parameters to a server.
        Loosely based on this article: https://realpython.com/python-sockets/

    Params
    ------
        host (str):
            Server hostname
        port (int):
            Server port
        record (bool):
            Whether the client should record all transmitted data.
        record_dir (str):
            Directory to which the recordings should be saved to.
        bodies_to_record (list or None):
            Body IDs that should be recorded. If None is provided, records
            all body IDs.
        is_producer (bool):
            Whether this client should be a producer (i.e. it will send
            data to the server instead of receiving them).
        angle (float):
            Angle (in deg) by which the global orientation should be rotated
            around the x-Axis in order to match Unity's default rotation.
        keep_yz_axes (bool):
            If True, y and z axes will not be swapped.
        npz_file (str or None):
            Path to a .npz file that contains the poses that should be
            broadcasted. Defaults to None.
        poses_attribute (str or None):
            If the client should act as producer with a provided npz file,
            this is the field under which the poses should be located.
            Defaults to None.
        shapes_attribute (str or None):
            If the client should act as producer with a provided npz file,
            this is the field under which the shapes should be located.
            Defaults to None.
        transl_attribute (str or None):
            If the client should act as producer with a provided npz file,
            this is the field under which the shapes should be located.
            Defaults to None.
        capture_fps_attribute (str or None):
            If the client should act as producer with a provided npz file,
            this is the field under which the mocap framerate is located.
            Defaults to None.
        target_fps (int):
            FPS which which the data should be send. Defaults to -1,
            which sets it to be the same as the capture fps.
        capture_fps (int):
            FPS with which the recorded data were captured. Overwrites
            the value obtained from capture_fps_attribute. Defaults to -1,
            which deactivates it and instead uses the information from the
            capture_fps_attribute field.
        drop_frames (bool):
            Whether frames should be dropped if the target replay framerate
            (target_fps) does not match the capture framerate. Requires that
            the capturing framerate is known. Defaults to False.
        loop (bool):
            Whether the motion sequence should be looped or only send once.
            Defaults to True.
        conversion_checkpoint (str or None):
            Path to the conversion checkpoint that should be used for
            conversions. Defaulst to None
        conversion_device (str):
            Device where the conversion should be calculated on. Defaults to
            'cpu'.
        verbosity (int):
            Verbosity level. Defaults to 1.
    """
    def __init__(self,
                 host: str,
                 port: int,
                 record: bool,
                 record_dir: str,
                 bodies_to_record: Union[List, None],
                 is_producer: bool,
                 angle: float,
                 keep_yz_axes: bool,
                 npz_file: Optional[str] = None,
                 poses_attribute: Optional[str] = None,
                 shapes_attribute: Optional[str] = None,
                 transl_attribute: Optional[str] = None,
                 capture_fps_attribute: Optional[str] = None,
                 target_fps: Optional[int] = -1,
                 capture_fps: Optional[int] = -1,
                 drop_frames: Optional[bool] = False,
                 loop: Optional[bool] = True,
                 conversion_checkpoint: Optional[str] = None,
                 conversion_device: Optional[str] = 'cpu',
                 calculate_conversion_errors: Optional[bool] = False,
                 verbosity: Optional[int] = 1
                 ):
        self.NUM_BETAS = 10     # Restriction of Unity's SMPL-X addon
        self.TRANSMISSION_MESSAGE_LENGTH_BYTES = 716  # Body ID 4 + Translation 3*4 + Betas 10 * 4 + Poses 165 * 4
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
        self.conversion_checkpoint = conversion_checkpoint
        self.conversion_device = conversion_device
        self.calculate_conversion_errors = calculate_conversion_errors
        if self.conversion_checkpoint is not None:
            if smpl_conversion_imported:
                self.converter = smpl_conversion.inference.CombinedPredictor(self.conversion_checkpoint,
                                                                             self.calculate_conversion_errors,
                                                                             self.conversion_device,
                                                                             body_model_location='D:\\data\\smpl_models',
                                                                             transfer_file_location='D:\\data\\smpl_models\\transfer')
                if self.converter.get_number_shape_components() is not None:
                    if self.converter.get_number_shape_components() < self.NUM_BETAS:
                        print(f"Warning: Loss of shape information! The loaded converter takes "
                              f"{self.converter.get_number_shape_components()} shape components as input, but there "
                              f"will be {self.NUM_BETAS} components transmitted over the network")
            else:
                raise ValueError("Cannot create conversion class because smpl_conversion package is not imported")
        else:
            self.converter = None

        # Whether our data only consist of a single set of shape parameters
        # or a batch of shape parameters for each pose
        self.single_shape_parameters = True
        mocap_fps = None
        if self.is_producer and self.record:
            raise ValueError(
                "The client cannot record and be a producer at the same time!"
            )
        if self.is_producer and npz_file:
            (
                self.poses, self.shapes, self.transl, mocap_fps
            ) = self._load_npz_attributes(
                npz_file,
                poses_attribute,
                shapes_attribute,
                transl_attribute,
                capture_fps_attribute
            )
            # Transform poses and translation to SMPL-X's coordinate system
            if self.poses is not None:
                pass
                #self._add_x_angle_offset_to_poses()
            if self.transl is not None and not self.keep_yz_axes:
                pass
                #self.transl = self._swap_translation_yz_axes(self.transl)
            if self.poses is None or self.shapes is None or self.transl is None:
                if self.verbosity > 0:
                    print("Defaulting to sending 1 and 0 sequences.")
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
        if self.target_fps == 0 or self.target_fps is None:
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
        self.time_last_transmission = None


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
                if isinstance(self.poses, np.ndarray) and isinstance(self.shapes, np.ndarray) and isinstance(self.transl, np.ndarray):
                    if poses_idx >= self.poses.shape[0]:
                        if not self.loop:
                            if self.verbosity > 0:
                                print(
                                    "Transmitted all poses, closing connection"
                                )
                            break
                        poses_idx = 0
                    # Transform pose to Unity's coordinate system
                    # compose one pose frame ???
                    current_pose = self.poses[poses_idx]
                    current_shape = self.shapes if self.single_shape_parameters else self.shapes[poses_idx]
                    current_transl = self.transl[poses_idx]
                    current_shape = self._adapt_betas_shape(current_shape)
                    if self.converter: # No converter when 3D model and transfer file path do not exist.
                        inp = DotMap({'trans': current_transl, 'betas': current_shape, 'poses': current_pose}, _dynamic=False)
                        pred, metric = self.converter.predict(inp, split_output=True, errors_to_calculate=['mpvpe'] if self.calculate_conversion_errors else None)
                        if metric is not None and metric > 0.05:
                            print(f"High Conversion MPVPE: {metric.item()}")
                        current_transl = pred.trans.cpu().numpy()[0]
                        current_shape = pred.betas.cpu().numpy()[0]
                        current_pose = pred.poses.cpu().numpy()[0]
                    data_to_transmit = np.hstack( # connect transl, beta and pose to one vec.
                        (
                            self._swap_translation_yz_axes_single(current_transl),
                            self._adapt_betas_shape(current_shape, True),
                            self._add_x_angle_offset_to_single_pose(current_pose)
                        ))
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
            recv_data = self.sock.recv(self.TRANSMISSION_MESSAGE_LENGTH_BYTES)
            if not recv_data:
                if self.verbosity > 0:
                    print("Server closed the connection")
                return False
            if self.is_producer:
                return True
            time_now = time.perf_counter()
            if self.time_last_transmission is not None and self.verbosity > 0:
                T = time_now - self.time_last_transmission
                f = 1 / T
                print(f"Receiving data with {f:.0f} Hz     ", end="\r", flush=True)
            self.time_last_transmission = time_now
            body_id = int.from_bytes(recv_data[:4], 'big')
            transl = np.frombuffer(recv_data[4:16], dtype=np.float32)
            shape = np.frombuffer(recv_data[16:56], dtype=np.float32)
            pose = np.frombuffer(recv_data[56:], dtype=np.float32)
            if self.verbosity > 1:
                print(f"Body index {body_id} | Translation: {transl} | "
                      f"Shape: {shape} | Pose shape: {pose.shape} "
                      f"| First 6 elements: {pose[:6]}")
            if self.record:
                if self.bodies_to_record is None or body_id in self.bodies_to_record:
                    curr_time = time.perf_counter()
                    if body_id not in list(self.recordings.keys()):
                        self.recordings[body_id] = {
                            'poses': [pose],
                            'transl': [transl],
                            'shapes': [shape],
                            'time_first_frame': curr_time,
                            'time_last_frame': curr_time
                        }
                    else:
                        self.recordings[body_id]['poses'].append(pose)
                        self.recordings[body_id]['transl'].append(transl)
                        self.recordings[body_id]['shapes'].append(shape)
                        self.recordings[body_id]['time_last_frame'] = curr_time
            return True
        if mask & selectors.EVENT_WRITE:
            if not self.is_producer:
                return True
            to_transmit = data_to_transmit
            if not isinstance(to_transmit, np.ndarray):
                to_transmit = np.zeros((178,), dtype=np.float32)
                if self.transmit_ones:
                    to_transmit += 1
                self.transmit_ones = not self.transmit_ones
            # to_sleep = self.time_to_sleep - (time.perf_counter() - time_last_transmission)
            # if to_sleep > 0:
            #     #print(f"Sleeping for {to_sleep} seconds")
            #     time.sleep(to_sleep)
            if self.verbosity > 1:
                print(f"Sending {to_transmit.shape} of size {len(to_transmit.tobytes())}")
            if self.time_last_transmission is not None:
                while True:
                    time_now = time.perf_counter()
                    if (time_now - self.time_last_transmission) >= self.time_to_sleep:
                        break
            else:
                time_now = time.perf_counter()
            self.sock.sendall(to_transmit.tobytes())
            if self.time_last_transmission is not None and self.verbosity > 0:
                T = time_now - self.time_last_transmission
                f = 1 / T
                print(f"Sending data with {f:.0f} Hz     ", end="\r", flush=True)
            self.time_last_transmission = time_now
            return time.perf_counter()


    # Read raw data from a .npz file
    def _load_npz_attributes(self, npz_file, pose_attribute, shape_attribute,
                             transl_attribute, capture_fps_attribute):
        if not osp.isfile(npz_file):
            print(f"ERROR: Not a valid file: {npz_file}", flush=True)
            return None, None, None, None
        content = np.load(npz_file) # read raw data into content.
        #print(f"[DEBUG] raw data content: {content.keys()}")
        # pose_attr: poses; shape_attr: betas; transl_attr: trans;
        # Check all need data exist
        if not pose_attribute in list(content.keys()):
            print(f"Error: Could not find pose attribute {pose_attribute} in {npz_file}", flush=True)
            return None, None, None, None
        if not shape_attribute in list(content.keys()):
            print(f"Error: Could not find shape attribute {shape_attribute} in {npz_file}", flush=True)
            return None, None, None, None
        if not transl_attribute in list(content.keys()):
            print(f"Error: Could not find translation attribute {transl_attribute} in {npz_file}", flush=True)
            return None, None, None, None
        
        shapes = content[shape_attribute].astype(np.float32)
        # print(f"shapes：{shapes.ndim}")
        if shapes.ndim == 1:
            self.single_shape_parameters = True
        else:
            self.single_shape_parameters = False
        return (
            content[pose_attribute].astype(np.float32),
            shapes,
            content[transl_attribute].astype(np.float32),
            content.get(capture_fps_attribute, default=None)
        )


    def _add_x_angle_offset_to_poses(self):
        """alternatively, see https://math.stackexchange.com/questions/382760/composition-of-two-axis-angle-rotations"""
        quats = quaternion.from_rotation_vector(self.poses[:, :3])
        rotX = quaternion.from_rotation_vector(np.asarray([1,0,0]) * np.deg2rad(self.x_rot_angle))
        self.poses[:, :3] = quaternion.as_rotation_vector(rotX * quats)



    def _add_x_angle_offset_to_single_pose(self,
                                           pose: np.ndarray
                                           ) -> np.ndarray:
        """
            Transforms the given pose to Unity's coordinate system by rotating
            around self.x_rot_angle degrees around the x axis

        Params
        ------
            pose (np.ndarray):
                Pose which should be transformed. Shape (N)

        Returns
        -------
            np.ndarray:
                The transformed pose. Shape (N)
        """
        quat = quaternion.from_rotation_vector(pose[:3])
        rotX = quaternion.from_rotation_vector(np.asarray([1,0,0]) * np.deg2rad(self.x_rot_angle))
        pose[:3] = quaternion.as_rotation_vector(rotX * quat)
        return pose


    def _save_recording(self):
        fname = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
        fpath = osp.join(self.record_dir, fname) + '.npz'
        if self.verbosity > 0:
            print(f"Saving recordings to {fpath}...", end=' ')
        rec_dir = {}
        for body_id in list(self.recordings.keys()):
            rec_dir[f'{body_id}_poses'] = np.stack(self.recordings[body_id]['poses'])
            rec_dir[f'{body_id}_shapes'] = np.stack(self.recordings[body_id]['shapes'])
            rec_dir[f'{body_id}_transl'] = np.stack(self.recordings[body_id]['transl'])
            time_elapsed = self.recordings[body_id]['time_last_frame'] - self.recordings[body_id]['time_first_frame']
            if time_elapsed <= 0:
                rec_dir[f'{body_id}_frame_rate'] = -1
            else:
                rec_dir[f'{body_id}_frame_rate'] = int(
                    rec_dir[f'{body_id}_poses'].shape[0] / time_elapsed
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


    def _swap_translation_yz_axes_single(self, arr):
        """
            Swap y and z axis to conform to Unity's coordinate system
        """
        return arr[[0,2,1]]


    def _adapt_betas_shape(self,
                           betas: np.ndarray,
                           ignore_converter: bool = False):
        """
            Adapts the given betas' shape to the requirements, e.g. depending
            on self.NUM_BETAS but also what a potentially available
            converter expects as input. If betas is smaller than the
            required output size, it will be padded with zeros.

        Params
        ------
            betas (np.ndarray):
                The shape parameters as a one-dimensional array of length n
            ignore_converter (bool):
                Whether the required shape calculation should ignore the
                converter. In this case, the returned shape parameters are
                always of the right shape for transmission.

        Result
        ------
            np.ndarray:
                The shape parameters as a one-dimensional array of length m,
                where m is either suited for direct transmission
                (m == self.NUM_BETAS) or for use in the conversion network
                stored in self.converter (m == converter.num_shape_components)
        """
        if self.converter and not ignore_converter:
            n_shape_components = self.converter.get_number_shape_components()
            if n_shape_components is None:
                raise ValueError("Could not infer required number of shape "
                                 "components for conversion network!")
            n = len(betas)
            delta_m = n_shape_components - n
            if delta_m == 0:
                return betas
            elif delta_m > 0:
                return np.hstack([betas, np.zeros(delta_m, dtype=np.float32)])
            elif delta_m < 0:
                return betas[:n_shape_components]
        else:
            if len(betas) >= self.NUM_BETAS:
                return betas[:self.NUM_BETAS]
            else:
                return np.hstack(betas, np.zeros(self.NUM_BETAS - len(betas),
                                                 dtype=np.float32))
        

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
    parser.add_argument('--shapes-field', type=str, default="betas",
                        help="<Optional> Name of the npz field that contains "
                        "the poses to broadcast. Defaults to 'betas'.")
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
    parser.add_argument('--conversion-checkpoint', type=str, default=None,
                        help="<Optional> Conversion checkpoint that should be "
                        "used to convert the input parameters to SMPL-X. "
                        "Defaults to None.")
    parser.add_argument('--conversion-device', type=str, default='cpu',
                        help="<Optional> Device where the conversion should be"
                        " calculated on. Defaults to 'cpu', which is faster "
                        "than 'cuda' for single real-time predictions.")
    parser.add_argument('--calc-conversion-errors', action='store_true',
                        help="Calculate conversion errors. Reduces performance.")
    args = parser.parse_args()
    bodies_to_record = None
    if args.bodies_to_record is not None:
        bodies_to_record = [int(x) for x in args.bodies_to_record]

    client = BodyPoseTcpClient(args.host, args.port, args.record, args.output,
                               bodies_to_record, args.producer, args.angle,
                               args.keep_yz_axes, args.data, args.poses_field,
                               args.shapes_field, args.transl_field,
                               args.mocap_fps_field, args.fps,
                               args.capture_fps, args.drop_frames,
                               not args.noloop, args.conversion_checkpoint,
                               args.conversion_device, args.calc_conversion_errors,
                               args.verbosity
    )
    client.connect()
    client.run()
