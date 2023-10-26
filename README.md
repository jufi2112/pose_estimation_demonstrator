# Pose Estimation Demonstrator

Tested with Unity 2021.3.19f1 on Windows 11 and Ubuntu 22.04

## Setup

1. Clone this repository and its submodules using `git clone --recurse-submodules https://github.com/jufi2112/pose_estimation_demonstrator`
2. `cd pose_estimation_demonstrator`
3. `git submodule update --force --init --remote --recursive`
4. Download the SMPL-X Unity Package from [here](https://smpl-x.is.tue.mpg.de/index.html) and put the extracted `SMPLX-Unity/Assets/SMPLX` folder into `unity/SMPL-X Pose Estimation Demonstrator/Assets`
    - If you don't already have an account, you have to create one and accept the license agreement
5. Download the `Pcx - Point Cloud Importer/Renderer for Unity` from [here](https://cloudstore.zih.tu-dresden.de/index.php/s/Kj8pyHJDjH5HENb)
    - Extract the file to `pose_estimation_demonstrator/unity/SMPL-X Pose Estimation Demonstrator/Packages`
    - This is a slightly modified version of the original `Pcx` addon by Keijiro Takahashi which can be found [here](https://github.com/keijiro/Pcx)
6. Some scenes of our demonstrator contain 3D scans of our offices, which we withhold at the moment
    - If you are associated with 6G-life and want to use the demonstrator with the 3D scans, write an email to Julien Fischer
7. Follow the setup instructions in the `rgbd-kinect-pose` repository provided [here](./pose_estimation/rgb-kinect-pose/readme.md)

## Usage
1. Start the server:
    - inside `pose_estimation_demonstrator/python/server` run `python tcp_server.py`
2. (Optional) If you want to observe what data is transmitted through the server, you can run `python tcp_client.py` from the `pose_estimation_demonstrator/python/server` directory
    - This requires a python environment with numpy installed
    - **Tip:** You can make a client act as a producer (and send poses from an existing `.npz` file, e.g. from the [AMASS](https://amass.is.tue.mpg.de/) dataset) by calling `python tcp_client.py --producer -d <path to the .npz file> --fps X` where X specifies the fps with which the poses should be send to the server
        - `python tcp_client.py -h` provides information on further parameters that may be necessary, depending on your `.npz` file.
3. Start the playback in the Unity `Prediction` scene
    - In order to use the latest scripts from GitHub, you may need to trigger a recompilation
    - You can tweak various settings in the already placed `TcpPuppeteer` and `smplx_male_tcp` prefabs
        - `TcpPuppeteer` will connect to the server and receive new translation and pose information, which it will distribute to registered `smplx_male_tcp` instances. The script exposes variables related to the TCP connection and for conversion between the producer's and Unity's coordinate systems
        - `smplx_male_tcp` represent a single body and the attached `TcpControlledBody` script contains different variables related to the body.
    - For now, you have to use the scene view in order to move the camera during playback
4. In `pose_estimation_demonstrator/pose_estimation/rgb-kinect-pose/src` run `./run_server.sh -c 10`
    - The pose estimation will take some time to start, even though it already produces output to the command line

# Used Software
We're using the following Unity packages inside of the demonstrator:
- SMPL-X Unity Package [link](https://smpl-x.is.tue.mpg.de/index.html)
- Pcx - Point Cloud Importer/Renderer for Unity [link](https://github.com/keijiro/Pcx)
