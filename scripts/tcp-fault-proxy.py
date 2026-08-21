#!/usr/bin/env python3
"""Qualification-only TCP proxy used to create deterministic connection partitions."""
import argparse, select, socket, threading
p=argparse.ArgumentParser(); p.add_argument('--listen-host',default='127.0.0.1'); p.add_argument('--listen-port',type=int,required=True); p.add_argument('--target-host',default='127.0.0.1'); p.add_argument('--target-port',type=int,required=True); a=p.parse_args()
def bridge(c):
    u=socket.create_connection((a.target_host,a.target_port),timeout=5); c.setblocking(False); u.setblocking(False); ss=[c,u]
    try:
        while True:
            r,_,x=select.select(ss,[],ss,1.0)
            if x:return
            for src in r:
                dst=u if src is c else c; data=src.recv(65536)
                if not data:return
                dst.sendall(data)
    finally:
        try:c.close()
        finally:u.close()
l=socket.socket(); l.setsockopt(socket.SOL_SOCKET,socket.SO_REUSEADDR,1); l.bind((a.listen_host,a.listen_port)); l.listen(128)
try:
    while True:
        c,_=l.accept(); threading.Thread(target=bridge,args=(c,),daemon=True).start()
finally:l.close()
