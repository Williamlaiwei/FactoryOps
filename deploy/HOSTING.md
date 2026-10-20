# Hosting comparison — checked 19 October 2026

For this low-traffic demonstration, one VPS can run web, the knowledge API and
both PostgreSQL databases. The database disks are included in the VPS disk; this
does not require separately billed managed PostgreSQL. All figures below exclude
tax, domain registration, extra backup storage and traffic overages. Providers
bill in the currencies shown; AUD totals depend on exchange rates.

| Option | Relevant monthly price | Assessment |
| --- | --- | --- |
| Hetzner Europe CX23, x86, 4 GB / 40 GB | EUR 5.49 + EUR 0.50 Primary IPv4 = EUR 5.99 | Lowest listed fixed price in this comparison; check capacity before choosing. The public cost-optimized page currently reports unavailability. European hosting adds distance for Australian users. |
| Hetzner Europe CX33, x86, 8 GB / 80 GB | EUR 8.49 + EUR 0.50 IPv4 = EUR 8.99 | More headroom, also subject to capacity. |
| DigitalOcean Basic, 4 GiB / 80 GiB | USD 24 | Straightforward fixed-cost option; Sydney is listed as SYD1. Check that the selected SKU is available in that region. |
| AWS Lightsail Linux IPv4, 4 GB / 80 GB | USD 24 | Similar fixed bundle. Verify the chosen region and transfer allowance at checkout. |
| Railway Hobby | USD 5 minimum, usage beyond that billed | Not a USD 5 all-inclusive package. RAM is USD 10/GB-month, CPU USD 20/vCPU-month, volumes USD 0.15/GB-month and egress USD 0.05/GB. Four services require separate setup instead of running this Compose bundle directly. |

The current local containers were observed around 300 MiB combined while idle;
that excludes host OS, proxy, build work and peak document indexing/search. It is
not evidence that a small-RAM plan will handle peaks. A 4 GB x86 VPS is a practical
demo starting estimate. Railway might stay close to its minimum with very light
usage, but needs measured running usage to quote a total. For example, 1 GB
average RAM alone costs USD 10/month before CPU/storage; this is a scenario, not
an estimate measured on Railway.

Recommendation: if lowest predictable monthly cost matters, try an available
Hetzner Europe CX23/CX33. If Australian demo responsiveness matters more, compare
DigitalOcean Sydney and Lightsail's suitable local region. Singapore Hetzner is
not the same cheap tier: its current CPX22 is EUR 26.49 before IPv4/tax. Recheck
price and stock when ordering. Avoid buying an ARM CAX instance for this prepared
Linux/amd64 package.

Official sources:

- [Hetzner current price adjustment table](https://docs.hetzner.com/general/infrastructure-and-availability/price-adjustment/)
- [Hetzner hardware and capacity](https://www.hetzner.com/cloud/cost-optimized/)
- [Hetzner IPv4 charge](https://docs.hetzner.com/cloud/servers/overview/)
- [DigitalOcean prices](https://www.digitalocean.com/pricing/droplets)
- [DigitalOcean regions](https://docs.digitalocean.com/platform/regional-availability/)
- [Lightsail prices](https://aws.amazon.com/lightsail/pricing/)
- [Railway resource pricing](https://docs.railway.com/pricing)
