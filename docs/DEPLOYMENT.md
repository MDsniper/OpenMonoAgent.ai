# Deploy with Xiaomi MiMo

The code and blank configuration template belong in GitHub. Your MiMo Token
Plan API key belongs in **`docker/.env` on the deployment server**. That file is
ignored by Git and excluded from the Docker build context.

## First deployment

Install Git, Docker Engine, and the Docker Compose plugin on the server. No GPU,
local model, or host .NET installation is required for this Docker workflow.

Clone your repository and create the private configuration:

```bash
git clone https://github.com/MDsniper/OpenMonoAgent.ai.git
cd OpenMonoAgent.ai
cp -n docker/.env.mimo.example docker/.env
chmod 600 docker/.env
nano docker/.env
```

Set this line inside **`docker/.env`**, replacing the placeholder with your key:

```dotenv
OPENMONO_API_KEY=your-mimo-token-plan-key
```

The template already selects `https://token-plan-sgp.xiaomimimo.com/v1` and
`mimo-v2.5-pro`. Set `WORKSPACE` to the absolute server path of the project you
want the agent to work on; the default is this checkout. Existing exported
environment variables override `.env` values, so remove stale overrides if you
previously configured another provider in your shell.

Build and launch from the repository root:

```bash
docker compose --env-file docker/.env -f docker/docker-compose.yml build agent
docker compose --env-file docker/.env -f docker/docker-compose.yml run --rm --no-deps agent --classic --no-acp
```

This runs the interactive coding agent in your SSH terminal. It uses MiMo for
inference and starts only the agent container. Agent state is mounted from the
server account's `~/.openmono` directory. This command does not start a persistent
web service or expose an editor API.

## Updating the server

Exit the agent, then run `git pull --ff-only` and repeat the build and launch
commands above. Your private `docker/.env` stays on the server across updates.
To rotate the key, edit that file and launch a new agent container.

For native .NET execution, see [Configuration](CONFIG.md). The .NET executable
does not load `.env` files itself; Docker Compose injects these values into the
container's environment.
