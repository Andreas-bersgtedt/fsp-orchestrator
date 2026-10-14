import {
  Badge,
  Button,
  Card,
  CardFooter,
  CardHeader,
  FluentProvider,
  Spinner,
  Text,
  Title1,
  Title2,
  webLightTheme,
} from '@fluentui/react-components'
import { useEffect, useState } from 'react'
import './App.css'

type HealthResponse = {
  status: string
  checkedAt: string
}

type HealthState =
  | { kind: 'loading' }
  | { kind: 'online'; response: HealthResponse }
  | { kind: 'offline' }

const workflows = [
  {
    title: 'Run prerequisites',
    description:
      'Validate this host, identity, Azure access, network paths, capacity, and FSP compatibility.',
    action: 'Open prerequisites',
  },
  {
    title: 'Register deployment',
    description:
      'Connect to an existing FSP Lite or Enterprise endpoint through its versioned HTTPS API.',
    action: 'Register FSP',
  },
  {
    title: 'Deploy to Azure',
    description:
      'Preview and apply an Enterprise deployment through ARM and the private AKS command API.',
    action: 'Create deployment',
  },
] as const

function App() {
  const [health, setHealth] = useState<HealthState>({ kind: 'loading' })

  useEffect(() => {
    const controller = new AbortController()

    async function loadHealth() {
      try {
        const response = await fetch('/api/health', {
          headers: { Accept: 'application/json' },
          signal: controller.signal,
        })
        if (!response.ok) {
          setHealth({ kind: 'offline' })
          return
        }
        setHealth({
          kind: 'online',
          response: (await response.json()) as HealthResponse,
        })
      } catch (error) {
        if (error instanceof DOMException && error.name === 'AbortError') {
          return
        }
        setHealth({ kind: 'offline' })
      }
    }

    void loadHealth()
    return () => controller.abort()
  }, [])

  return (
    <FluentProvider theme={webLightTheme}>
      <div className="app-shell">
        <header className="top-bar">
          <div>
            <Text size={200} weight="semibold" className="product-kicker">
              FABRIC SHORTCUT PROXY
            </Text>
            <Text size={500} weight="semibold">
              Orchestrator
            </Text>
          </div>
          <HealthBadge health={health} />
        </header>

        <main>
          <section className="hero">
            <Text size={300} weight="semibold" className="eyebrow">
              LOCAL MANAGEMENT PLANE
            </Text>
            <Title1>Operate every FSP deployment from one place.</Title1>
            <Text size={500} className="hero-copy">
              Register existing environments or deploy to Azure through remote,
              authenticated control-plane APIs. No access to FSP filesystems is
              required.
            </Text>
          </section>

          <section aria-labelledby="workflow-heading">
            <div className="section-heading">
              <div>
                <Title2 id="workflow-heading">Start a workflow</Title2>
                <Text>Choose the next operation for your FSP estate.</Text>
              </div>
              <Badge appearance="outline">FSP 3.1.0 contracts loaded</Badge>
            </div>

            <div className="workflow-grid">
              {workflows.map((workflow) => (
                <Card key={workflow.title} className="workflow-card">
                  <CardHeader
                    header={<Text weight="semibold">{workflow.title}</Text>}
                    description={<Text>{workflow.description}</Text>}
                  />
                  <CardFooter>
                    <Button appearance="primary" disabled>
                      {workflow.action}
                    </Button>
                  </CardFooter>
                </Card>
              ))}
            </div>
          </section>
        </main>

        <footer>
          <Text size={200}>
            Remote management boundary: ARM, AKS command, Key Vault, and FSP
            HTTPS APIs.
          </Text>
        </footer>
      </div>
    </FluentProvider>
  )
}

function HealthBadge({ health }: { health: HealthState }) {
  if (health.kind === 'loading') {
    return <Spinner size="tiny" label="Checking local service" />
  }

  return health.kind === 'online' ? (
    <Badge appearance="filled" color="success">
      Local service online
    </Badge>
  ) : (
    <Badge appearance="filled" color="danger">
      Local service unavailable
    </Badge>
  )
}

export default App
