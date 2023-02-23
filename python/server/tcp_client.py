import socket
import time
import numpy as np
import argparse

class BodyPoseTcpClient:
    """Loosely based on this article: https://realpython.com/python-sockets/"""
    def __init__(self, host, port, is_producer, sleep_interval = 0):
        self.host = host
        self.port = port
        self.is_producer = is_producer
        self.transmit_ones = True
        self.sleep_interval = sleep_interval


    def connect(self):
        server_addr = (self.host, self.port)
        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as sock:
            sock.connect((self.host, self.port))
            while (True):
                if self.is_producer:
                    to_transmit = np.zeros((165,), dtype=np.float32)
                    if self.transmit_ones:
                        to_transmit += 1
                    self.transmit_ones = not self.transmit_ones
                    sock.sendall(to_transmit.tobytes())
                    time.sleep(self.sleep_interval)
                else:
                    data = sock.recv(664)
                    body_id = int.from_bytes(data[:4], 'big')
                    arr = np.frombuffer(data[4:], dtype=np.float32)
                    print(f"Body index {body_id} | Data: {arr.shape} | First "
                          f"10 elements: {arr[:10]}")


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description="TCP Client")
    parser.add_argument('--host', type=str, default="localhost",
                        help="Host to connect to. Optional, defaults to "
                        "localhost")
    parser.add_argument('-p', '--port', type=int, default=7777,
                        help="Port to connect to. Optional, defaults to "
                        "7777")
    parser.add_argument('--producer', action='store_true', help="Defines that "
                        "this client is going to be a producer")
    parser.add_argument('-s', '--sleep', type=int, default=5,
                        help="If client acts as a producer, this defines the "
                        "seconds to wait until another pose is transmitted")
    args = parser.parse_args()
    client = BodyPoseTcpClient(args.host, args.port, args.producer, args.sleep)
    client.connect()
    