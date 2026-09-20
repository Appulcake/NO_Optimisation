### NO Optimisation
Bunch of optimisations for both client and (especially headless) server.

Most of the client optimisations are distance based, with the goal that if something is far from you, that it can be updated less often (or not at all) without a noticeable effect.
On both the client and server optimisations, some functions are disabled that are entirely redundant to run and just caused lower performance for no reason.

Every part can be toggled in configs. When ran on client, headless server configs aren't visible, and vice versa on headless server where client only configs aren't populated to prevent clutter.
Client configs can be changed live, server/headless server config changes need a server restart.

One day I'm hopefully not tired and stop putting off writing a more comprehensive readme + config descriptions.
