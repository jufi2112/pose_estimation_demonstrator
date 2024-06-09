import socket
import sys
import selectors
import types
import numpy as np
import argparse
from copy import deepcopy

class BodyPoseTcpServer:
    """Loosely based on this article: https://realpython.com/python-sockets/"""
    def __init__(self, host, port, connections_to_accept,
                 send_initial_transmissions):
        self.TRANSMISSION_MESSAGE_LENGTH_BYTES = 712  # Translation 3*4 + Betas 10 * 4 + Poses 165 * 4 ## No body IDs in transmissions from clients
        self.host = host
        self.port = port
        self.connections_to_accept = connections_to_accept
        self.sel = selectors.DefaultSelector()
        self.lsock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        self.lsock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        self.consumers = []
        self.clients = {}
        self.body_params = {}
        self.send_initial_transmissions = send_initial_transmissions
        self.first_transmissions = {}
        self.next_free_body_id = 1


    def start_server(self, verbosity):
        """Starts the server

        Args:
            verbosity (int): Verbosity level. 0: No status messages.
                1: Important status messages. 2: All messages.
        """
        self.lsock.bind((self.host, self.port))
        self.lsock.listen(self.connections_to_accept)
        if verbosity > 0:
            print(f"Listening on {(self.host, self.port)}")
        self.lsock.setblocking(False)
        self.sel.register(
            fileobj=self.lsock,
            events=selectors.EVENT_READ,
            data=None
        )
        try:
            while True:
                events = self.sel.select(timeout=None)
                for key, mask in events:
                    if key.data is None:
                        self._accept_wrapper(key.fileobj, verbosity)
                    else:
                        self._service_connection(key, mask, verbosity)
        except KeyboardInterrupt:
            if verbosity > 0:
                print("Stopping server...")
        finally:
            self.sel.close()
        

    def _accept_wrapper(self, sock, verbosity):
        conn, addr = sock.accept()
        if verbosity > 0:
            print(f"Accepted connection from {addr}")
        conn.setblocking(False)
        self.clients[conn] = {'addr': addr, 'body_id': 0}
        self.consumers.append(conn)
        data = types.SimpleNamespace(addr=addr, producer=False,
                                     recv_init_transm=not self.send_initial_transmissions)
        # Whenever a new client connects, we want to send the first transmissions
        # of all active producers to this client, so that the client can correctly
        # calculate the position difference that it has to apply to its base position
        if self.send_initial_transmissions:
            events = selectors.EVENT_WRITE
        else:
            events = selectors.EVENT_READ
        self.sel.register(conn, events, data=data)


    def _service_connection(self, key, mask, verbosity):
        sock = key.fileobj
        data = key.data
        if mask & selectors.EVENT_WRITE:
            if not data.recv_init_transm and self.send_initial_transmissions:
                self._send_initial_transmissions(sock)
                data.recv_init_transm = True
            else:
                if verbosity > 0:
                    print(
                        "Received write event for client with recv_init_transm"
                        f" = True. Client: {self.clients[sock]['addr']}"
                    )
            self.sel.modify(sock, selectors.EVENT_READ, data)
        if mask & selectors.EVENT_READ:
            recv_data = sock.recv(self.TRANSMISSION_MESSAGE_LENGTH_BYTES)
            if recv_data:
                if not data.producer:
                    # client is now a producer
                    data.producer = True
                    try:
                        self.consumers.remove(sock)
                    except ValueError:
                        print(
                            'Could not remove transmitting client from '
                            'consumer list'
                        )
                    self.clients[sock]['body_id'] = self.next_free_body_id
                    self.first_transmissions[self.next_free_body_id] = \
                        deepcopy(recv_data)
                    if verbosity > 0:
                        print(f"Assigned body ID {self.next_free_body_id} to "
                            f"{self.clients[sock]['addr']}"
                        )
                    self.next_free_body_id += 1
                curr_body_id = self.clients[sock]['body_id']
                self.body_params[curr_body_id] = recv_data
                self._update_consumers(curr_body_id, verbosity)
            else:
                if verbosity > 0:
                    print(f"Client at {data.addr} closed the connection.")
                self.sel.unregister(sock)
                try:
                    self.consumers.remove(sock)
                except ValueError:
                    pass
                if data.producer:
                    del self.first_transmissions[self.clients[sock]['body_id']]
                del self.clients[sock]
                sock.close()


    def _update_consumers(self, body_id, verbosity):
        for sock in self.consumers:
            data_to_transmit = int(body_id).to_bytes(4, 'big') \
                + self.body_params[body_id]
            if verbosity > 1:
                print(f"Transmitting to {self.clients[sock]['addr']}")
            sock.sendall(data_to_transmit)


    def _send_initial_transmissions(self, sock):
        for body_id, msg in self.first_transmissions.items():
            data_to_transmit = int(body_id).to_bytes(4, 'big') + msg
            sock.sendall(data_to_transmit)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(
        description="Non-blocking TCP Server that returns a SMPL-X Pose on "
                    "request."
    )
    parser.add_argument('--host', type=str, default="localhost",
                        help="Host IPv4 address. Optional, defaults to "
                        "localhost")
    parser.add_argument('-p', '--port', type=int, default=7777,
                        help="Port. Optional, defaults to 7777")
    parser.add_argument('-c', '--connections', type=int, default=10,
                        help="Number of connections to accept. Optional, "
                        "defaults to 10")
    parser.add_argument('--no-initial-transmissions', action='store_true',
                        help="If specified, no recorded initial transmissions "
                        "will be transmitted on initial contact.")
    parser.add_argument('-v', '--verbosity', type=int, default=1,
                        help="Verbosity level. Optional, defaults to 1")
    args = parser.parse_args()
    server = BodyPoseTcpServer(args.host, args.port, args.connections,
                               not args.no_initial_transmissions)
    server.start_server(args.verbosity)
    