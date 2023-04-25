# Pose Estimation Demonstrator

## Setup

1. Clone this repository and its submodules using `git clone --recurse-submodules https://github.com/jufi2112/pose_estimation_demonstrator`
2. `cd pose_estimation_demonstrator`
3. `git submodules update --force --init --remote --recursive`
4. Download the Unity SMPL-X Prediction project from [here](https://cloudstore.zih.tu-dresden.de/index.php/s/cBEstNYb8AkqKEP) and extract it into the `pose_estimation_demonstrator/unity/` subdirectory.
    - Merge the directories if prompted
    - Since some scenes of our demonstrator contain 3D scans of our offices, we withhold these assets.
        - If you are associated with 6G-life and want to use the demonstrator with the 3D scans, write a mail to Julien Fischer
    - Note: The `.zip` file contains files that are too large to upload to GitHub
5. Follow the setup instructions in the `rgbd-kinect-pose` repository provided [here](./pose_estimation/rgb-kinect-pose/readme.md)

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